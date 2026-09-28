using DroneLogger.Classes;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DroneLogger
{
    public class Drone3DView
    {
        private Viewport3D _viewport = null!;
        private PerspectiveCamera _camera = null!;
        private double _azimuthDeg = 45;
        private double _elevationDeg = 20;
        private double _distance = 3;
        private Point3D _cameraTarget;
        private Point _lastDragPosition;
        private bool _isDragging;
        private const double MinCameraDistance = 0.35;
        private const double MaxCameraDistance = 8.0;
        private const double ModelHeadingCorrectionDegrees = 90.0;
        private Model3DGroup _models = null!;
        private Model3DGroup _droneModel = null!;
        private double _droneBottomOffset;
        private double _initAltitude = 0.0;
        private double _initYaw = 0.0;
        private double _initPitch = 0.0;
        private double _initRoll = 0.0;
        private Border _border = null!;

        /// <summary>
        /// Initialize and add the 3D view into the provided container (a Grid from XAML).
        /// </summary>
        public void Initialize(
            Grid container,
            double initialAltitude = 0.0,
            double initialYaw = 0.0,
            double initialPitch = 0.0,
            double initialRoll = 0.0,
            bool showFrame = true,
            double initialCameraDistance = 3.0,
            double cameraTargetHeight = 0.0,
            bool enableMouseWheelZoom = false,
            bool enableMouseDragPan = false)
        {
            if (container == null) return;

            // store initial pose
            _initAltitude = initialAltitude;
            _initYaw = initialYaw;
            _initPitch = initialPitch;
            _initRoll = initialRoll;
            _distance = Math.Clamp(
                initialCameraDistance,
                MinCameraDistance,
                MaxCameraDistance);
            _cameraTarget = new Point3D(
                0.0,
                Math.Max(0.0, cameraTargetHeight),
                0.0);

            // Create border with black outline as rendering area
            _border = new Border
            {
                BorderBrush = showFrame ? Brushes.Black : Brushes.Transparent,
                BorderThickness = showFrame ? new Thickness(1) : new Thickness(0),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = showFrame ? new Thickness(10) : new Thickness(0)
            };

            // Create Viewport3D
            _viewport = new Viewport3D();

            // Camera (created as a field so it can be manipulated later)
            _camera = new PerspectiveCamera
            {
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45,
                NearPlaneDistance = 0.1,
                FarPlaneDistance = 200
            };
            _viewport.Camera = _camera;
            // Set a comfortable angled view so the drone perspective is visible.
            SetCameraView(_azimuthDeg, _elevationDeg, _distance);

            // Lights
            _models = new Model3DGroup();
            // Ambient so sides are visible
            _models.Children.Add(new AmbientLight(Color.FromRgb(40, 40, 40)));
            // Directional to provide shading
            _models.Children.Add(new DirectionalLight(Color.FromRgb(230, 230, 230), new Vector3D(-1, -1, -2)));

            string droneModelPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "Drone", "scene.gltf");
            LoadedDroneModel loadedDrone = GltfDroneModelLoader.Load(droneModelPath);
            _droneModel = loadedDrone.Model;
            _droneBottomOffset = -loadedDrone.LowestPoint;
            _models.Children.Add(_droneModel);

            // Apply the requested initial altitude and orientation to the drone.
            ApplyInitialTransform();

            // Add the model group to the viewport
            var modelVisual = new ModelVisual3D { Content = _models };
            _viewport.Children.Add(modelVisual);

            // Add a reference grid at altitude 0 (bigger than the box)
            var referenceGrid = CreateReferenceGrid(1.0, 0.0, 0.1);
            if (referenceGrid != null)
            {
                _models.Children.Add(referenceGrid);
            }

            // Add axis lines (X=red, Y=green, Z=blue)
            var axes = CreateAxes(1.0);
            if (axes != null)
            {
                _models.Children.Add(axes);
            }

            // Put viewport into border and add to container
            _border.Child = _viewport;
            if (enableMouseWheelZoom)
            {
                _border.PreviewMouseWheel += Border_PreviewMouseWheel;
            }
            if (enableMouseDragPan)
            {
                _border.Cursor = Cursors.SizeAll;
                _border.PreviewMouseLeftButtonDown += Border_PreviewMouseLeftButtonDown;
                _border.PreviewMouseMove += Border_PreviewMouseMove;
                _border.PreviewMouseLeftButtonUp += Border_PreviewMouseLeftButtonUp;
                _border.LostMouseCapture += Border_LostMouseCapture;
            }
            container.Children.Add(_border);
        }

        private void Border_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 0.85 : 1.15;
            _distance = Math.Clamp(
                _distance * zoomFactor,
                MinCameraDistance,
                MaxCameraDistance);
            SetCameraView(_azimuthDeg, _elevationDeg, _distance);

            // Keep the parent chart ScrollViewer from scrolling while the
            // pointer is deliberately zooming this compact 3D view.
            e.Handled = true;
        }

        private void Border_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            _isDragging = true;
            _lastDragPosition = e.GetPosition(_border);
            _border.CaptureMouse();
            e.Handled = true;
        }

        private void Border_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPosition = e.GetPosition(_border);
            Vector drag = currentPosition - _lastDragPosition;
            _lastDragPosition = currentPosition;

            if (drag.LengthSquared < double.Epsilon)
            {
                return;
            }

            double viewportHeight = Math.Max(_border.ActualHeight, 1.0);
            double visibleHeightAtTarget =
                2.0 * _distance *
                Math.Tan(_camera.FieldOfView * Math.PI / 360.0);
            double worldUnitsPerPixel = visibleHeightAtTarget / viewportHeight;

            Vector3D forward = _camera.LookDirection;
            forward.Normalize();
            Vector3D right = Vector3D.CrossProduct(
                forward,
                _camera.UpDirection);
            right.Normalize();
            Vector3D screenUp = Vector3D.CrossProduct(right, forward);
            screenUp.Normalize();

            // Translate the camera and its target opposite the horizontal drag
            // and with the vertical drag so the scene follows the pointer.
            Vector3D targetOffset =
                (-right * drag.X + screenUp * drag.Y) *
                worldUnitsPerPixel;
            _cameraTarget += targetOffset;
            SetCameraView(_azimuthDeg, _elevationDeg, _distance);
            e.Handled = true;
        }

        private void Border_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                EndMouseDrag();
                e.Handled = true;
            }
        }

        private void Border_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void EndMouseDrag()
        {
            _isDragging = false;
            if (_border.IsMouseCaptured)
            {
                _border.ReleaseMouseCapture();
            }
        }

        // Update the 3D model based on incoming log data (roll, pitch, yaw, altitude)
        public void Update(LogData data)
        {
            if (data == null || _droneModel == null) return;

            // Ensure update happens on UI thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var tg = new Transform3DGroup();

                // Align the model's nose with the simulator's forward axis before
                // applying telemetry rotations in world coordinates.
                tg.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(
                        new Vector3D(0, 1, 0),
                        ModelHeadingCorrectionDegrees)));

                // Apply rotations: roll (X), pitch (Z), yaw (Y).
                var rollRotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -data.roll);
                var pitchRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), -data.pitch);
                var yawRotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), -data.yaw);

                tg.Children.Add(new RotateTransform3D(pitchRotation));
                tg.Children.Add(new RotateTransform3D(rollRotation));
                tg.Children.Add(new RotateTransform3D(yawRotation));

                // Translate so the bottom of the drone sits at the reported altitude.
                // altitude from LogData is scaled by 0.01 in world units
                double altitudeWorld = data.altitude * 0.01;
                tg.Children.Add(new TranslateTransform3D(
                    0, altitudeWorld + _droneBottomOffset, 0));

                _droneModel.Transform = tg;
            });
        }

        private void ApplyInitialTransform()
        {
            if (_droneModel == null) return;

            var tg = new Transform3DGroup();

            tg.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(
                    new Vector3D(0, 1, 0),
                    ModelHeadingCorrectionDegrees)));

            var rollRotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), _initRoll);
            var pitchRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), _initPitch);
            var yawRotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _initYaw);

            tg.Children.Add(new RotateTransform3D(pitchRotation));
            tg.Children.Add(new RotateTransform3D(rollRotation));
            tg.Children.Add(new RotateTransform3D(yawRotation));

            double altitudeWorld = _initAltitude * 0.01;
            tg.Children.Add(new TranslateTransform3D(
                0, altitudeWorld + _droneBottomOffset, 0));

            _droneModel.Transform = tg;

        }

        /// <summary>
        /// Set camera spherical coordinates (azimuth/yaw, elevation/pitch, distance) to view the scene.
        /// Azimuth: degrees around Y axis (0 = +Z), Elevation: degrees above XZ plane.
        /// </summary>
        public void SetCameraView(double azimuthDegrees, double elevationDegrees, double distance)
        {
            if (_camera == null) return;

            // Convert degrees to radians
            double az = azimuthDegrees * Math.PI / 180.0;
            double el = elevationDegrees * Math.PI / 180.0;

            // Spherical to Cartesian (camera looks at origin)
            double x = distance * Math.Cos(el) * Math.Sin(az);
            double y = distance * Math.Sin(el);
            double z = distance * Math.Cos(el) * Math.Cos(az);

            // Orbit around the current target. Moving the target pans the scene
            // without changing the viewing angle or zoom distance.
            _camera.Position = _cameraTarget + new Vector3D(x, y, z);
            _camera.LookDirection = new Vector3D(-x, -y, -z);
        }

        /// <summary>
        /// Create a grid on the XZ plane centered at origin. cellSize controls spacing between lines.
        /// </summary>
        private Model3DGroup CreateReferenceGrid(double halfSize, double altitude, double cellSize)
        {
            var group = new Model3DGroup();
            var color = Color.FromArgb(80, 200, 200, 200); // very light
            var mat = new DiffuseMaterial(new SolidColorBrush(color));
            // Create grid lines as thin rectangular boxes so they render correctly in 3D.
            int lines = (int)Math.Ceiling((halfSize * 2) / cellSize);
            double start = -halfSize;
            double span = halfSize * 2.0;

            // thickness of a grid line (world units)
            double lineHeight = 0.001; // small vertical thickness so it's visible
            double lineThickness = Math.Max(cellSize * 0.05, 0.01); // thickness in X/Z extent

            for (int i = 0; i <= lines; i++)
            {
                double offset = start + i * cellSize;

                // line along X (varying Z) - long box along X, thin in Z
                var xMesh = CreateBoxMesh(span, lineHeight, lineThickness);
                var xModel = new GeometryModel3D(xMesh, mat) { BackMaterial = mat };
                xModel.Transform = new TranslateTransform3D(0, altitude, offset);
                group.Children.Add(xModel);

                // line along Z (varying X) - long box along Z, thin in X
                var zMesh = CreateBoxMesh(lineThickness, lineHeight, span);
                var zModel = new GeometryModel3D(zMesh, mat) { BackMaterial = mat };
                zModel.Transform = new TranslateTransform3D(offset, altitude, 0);
                group.Children.Add(zModel);
            }

            return group;
        }

        /// <summary>
        /// Create simple colored axes as thin rectangular boxes (so they are visible in 3D).
        /// </summary>
        private Model3DGroup CreateAxes(double length)
        {
            var group = new Model3DGroup();

            double thickness = 0.003;
            // X axis (red) - along +X
            var xMesh = CreateBoxMesh(length, thickness, thickness);
            var xMat = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
            var xModel = new GeometryModel3D(xMesh, xMat) { BackMaterial = xMat };
            xModel.Transform = new TranslateTransform3D(length / 2.0, 0, 0);
            group.Children.Add(xModel);

            // Y axis (green) - along +Y
            var yMesh = CreateBoxMesh(thickness, length, thickness);
            var yMat = new DiffuseMaterial(new SolidColorBrush(Colors.Green));
            var yModel = new GeometryModel3D(yMesh, yMat) { BackMaterial = yMat };
            yModel.Transform = new TranslateTransform3D(0, length / 2.0, 0);
            group.Children.Add(yModel);

            // Z axis (blue) - along +Z
            var zMesh = CreateBoxMesh(thickness, thickness, length);
            var zMat = new DiffuseMaterial(new SolidColorBrush(Colors.Blue));
            var zModel = new GeometryModel3D(zMesh, zMat) { BackMaterial = zMat };
            zModel.Transform = new TranslateTransform3D(0, 0, length / 2.0);
            group.Children.Add(zModel);

            return group;
        }

        private MeshGeometry3D CreateBoxMesh(double width, double height, double depth)
        {
            var hw = width / 2.0;
            var hh = height / 2.0;
            var hd = depth / 2.0;

            var mesh = new MeshGeometry3D();

            // 8 corners
            var p0 = new Point3D(-hw, -hh, -hd);
            var p1 = new Point3D(hw, -hh, -hd);
            var p2 = new Point3D(hw, hh, -hd);
            var p3 = new Point3D(-hw, hh, -hd);
            var p4 = new Point3D(-hw, -hh, hd);
            var p5 = new Point3D(hw, -hh, hd);
            var p6 = new Point3D(hw, hh, hd);
            var p7 = new Point3D(-hw, hh, hd);

            // Helper to add a face (two triangles)
            void AddFace(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                int index = mesh.Positions.Count;
                mesh.Positions.Add(a);
                mesh.Positions.Add(b);
                mesh.Positions.Add(c);
                mesh.Positions.Add(d);

                // normal calculation (approx)
                var normal = Vector3D.CrossProduct(b - a, c - a);
                normal.Normalize();

                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);

                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + 2);
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 2);
                mesh.TriangleIndices.Add(index + 3);
            }

            // front (z+)
            AddFace(p5, p4, p7, p6);
            // back (z-)
            AddFace(p0, p1, p2, p3);
            // left (x-)
            AddFace(p4, p0, p3, p7);
            // right (x+)
            AddFace(p1, p5, p6, p2);
            // top (y+)
            AddFace(p3, p2, p6, p7);
            // bottom (y-)
            AddFace(p4, p5, p1, p0);

            return mesh;
        }
    }
}
