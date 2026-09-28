using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DroneLogger.Classes;

/// <summary>
/// Loads the small glTF feature set used by the bundled drone model into WPF 3D.
/// The model is converted from its Z-up coordinates to WPF's Y-up coordinates.
/// </summary>
internal static class GltfDroneModelLoader
{
    private const double ModelScale = 0.09;

    internal static LoadedDroneModel Load(string gltfPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(gltfPath));
        JsonElement root = document.RootElement;
        string modelDirectory = Path.GetDirectoryName(gltfPath)!;
        byte[] binaryBuffer = File.ReadAllBytes(
            Path.Combine(modelDirectory, root.GetProperty("buffers")[0]
                .GetProperty("uri").GetString()!));

        JsonElement accessors = root.GetProperty("accessors");
        JsonElement bufferViews = root.GetProperty("bufferViews");
        JsonElement materials = root.GetProperty("materials");
        JsonElement meshes = root.GetProperty("meshes");

        var model = new Model3DGroup();
        double lowestPoint = double.PositiveInfinity;

        foreach (JsonElement mesh in meshes.EnumerateArray())
        {
            foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                // WPF's GeometryModel3D renders triangle lists. This model has
                // one tiny line primitive, which is intentionally skipped.
                if (primitive.TryGetProperty("mode", out JsonElement mode) &&
                    mode.GetInt32() != 4)
                {
                    continue;
                }

                JsonElement attributes = primitive.GetProperty("attributes");
                int positionAccessorIndex = attributes.GetProperty("POSITION").GetInt32();
                int? normalAccessorIndex = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessor)
                    ? normalAccessor.GetInt32()
                    : null;

                var positions = ReadVector3Accessor(
                    binaryBuffer, accessors[positionAccessorIndex], bufferViews);
                var normals = normalAccessorIndex.HasValue
                    ? ReadVector3Accessor(binaryBuffer, accessors[normalAccessorIndex.Value], bufferViews)
                    : null;
                var indices = primitive.TryGetProperty("indices", out JsonElement indexAccessor)
                    ? ReadIndexAccessor(binaryBuffer, accessors[indexAccessor.GetInt32()], bufferViews)
                    : CreateSequentialIndices(positions.Count);

                var geometry = new MeshGeometry3D();
                for (int i = 0; i < positions.Count; i++)
                {
                    Point3D point = ConvertPosition(positions[i]);
                    geometry.Positions.Add(point);
                    lowestPoint = Math.Min(lowestPoint, point.Y);

                    if (normals != null)
                    {
                        geometry.Normals.Add(ConvertNormal(normals[i]));
                    }
                }

                foreach (int index in indices)
                {
                    geometry.TriangleIndices.Add(index);
                }

                int materialIndex = primitive.TryGetProperty("material", out JsonElement material)
                    ? material.GetInt32()
                    : -1;
                MaterialGroup materialGroup = CreateMaterial(materials, materialIndex);
                model.Children.Add(new GeometryModel3D(geometry, materialGroup)
                {
                    BackMaterial = materialGroup
                });
            }
        }

        if (model.Children.Count == 0)
        {
            throw new InvalidDataException("The drone model does not contain any triangle meshes.");
        }

        return new LoadedDroneModel(model, lowestPoint);
    }

    private static Point3D ConvertPosition(Vector3 value) => new(
        value.X * ModelScale,
        value.Z * ModelScale,
        -value.Y * ModelScale);

    private static Vector3D ConvertNormal(Vector3 value) => new(
        value.X,
        value.Z,
        -value.Y);

    private static List<Vector3> ReadVector3Accessor(
        byte[] buffer,
        JsonElement accessor,
        JsonElement bufferViews)
    {
        if (accessor.GetProperty("componentType").GetInt32() != 5126 ||
            accessor.GetProperty("type").GetString() != "VEC3")
        {
            throw new InvalidDataException("The drone model uses an unsupported vector format.");
        }

        JsonElement bufferView = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
        int stride = bufferView.TryGetProperty("byteStride", out JsonElement byteStride)
            ? byteStride.GetInt32()
            : 12;
        int start = GetBufferStart(accessor, bufferView);
        int count = accessor.GetProperty("count").GetInt32();
        var values = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            int offset = start + (i * stride);
            values.Add(new Vector3(
                ReadSingle(buffer, offset),
                ReadSingle(buffer, offset + 4),
                ReadSingle(buffer, offset + 8)));
        }

        return values;
    }

    private static List<int> ReadIndexAccessor(
        byte[] buffer,
        JsonElement accessor,
        JsonElement bufferViews)
    {
        JsonElement bufferView = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
        int componentType = accessor.GetProperty("componentType").GetInt32();
        int componentSize = componentType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => throw new InvalidDataException("The drone model uses an unsupported index format.")
        };

        int start = GetBufferStart(accessor, bufferView);
        int count = accessor.GetProperty("count").GetInt32();
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            int offset = start + (i * componentSize);
            indices.Add(componentType switch
            {
                5121 => buffer[offset],
                5123 => BitConverter.ToUInt16(buffer, offset),
                5125 => checked((int)BitConverter.ToUInt32(buffer, offset)),
                _ => 0
            });
        }

        return indices;
    }

    private static int GetBufferStart(JsonElement accessor, JsonElement bufferView)
    {
        int bufferViewOffset = bufferView.TryGetProperty("byteOffset", out JsonElement viewOffset)
            ? viewOffset.GetInt32()
            : 0;
        int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement accessorByteOffset)
            ? accessorByteOffset.GetInt32()
            : 0;
        return bufferViewOffset + accessorOffset;
    }

    private static List<int> CreateSequentialIndices(int count)
    {
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        return indices;
    }

    private static MaterialGroup CreateMaterial(JsonElement materials, int materialIndex)
    {
        Color baseColor = Color.FromRgb(150, 150, 150);
        if (materialIndex >= 0 && materialIndex < materials.GetArrayLength())
        {
            JsonElement material = materials[materialIndex];
            if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) &&
                pbr.TryGetProperty("baseColorFactor", out JsonElement colorFactor) &&
                colorFactor.GetArrayLength() >= 3)
            {
                baseColor = Color.FromScRgb(
                    1.0f,
                    colorFactor[0].GetSingle(),
                    colorFactor[1].GetSingle(),
                    colorFactor[2].GetSingle());
            }
        }

        var materialGroup = new MaterialGroup();
        materialGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(baseColor)));
        materialGroup.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Colors.White), 20));
        return materialGroup;
    }

    private static float ReadSingle(byte[] buffer, int offset) =>
        BitConverter.ToSingle(buffer, offset);

    private readonly record struct Vector3(float X, float Y, float Z);
}

internal sealed record LoadedDroneModel(Model3DGroup Model, double LowestPoint);
