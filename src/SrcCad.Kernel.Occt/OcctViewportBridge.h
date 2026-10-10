#pragma once

namespace SrcCad::Kernel::Occt
{
    public enum class CadSelectionMode
    {
        Object = 0,
        Vertex = 1,
        Edge = 2,
        Face = 4
    };

    public value struct CadPoint3D
    {
        double X;
        double Y;
        double Z;
    };

    class OcctViewportState;

    public ref class OcctViewportBridge sealed
    {
    public:
        OcctViewportBridge();
        ~OcctViewportBridge();
        !OcctViewportBridge();

        bool Init(System::IntPtr windowHandle);
        bool IsInitialized();
        void Resize();
        void Redraw();
        void FitAll();
        void SetOrthographic(bool enabled);
        void SetFrontView();
        void SetTopView();
        void SetRightView();
        void SetIsoView();
        void SetNormalView(double nx, double ny, double nz,
                           double upx, double upy, double upz);

        void StartRotation(int x, int y);
        void Rotate(int x, int y);
        void Pan(int dx, int dy);
        void ZoomWheel(int delta);

        void MoveTo(int x, int y);
        bool SelectDetected(bool toggle);
        void ClearSelection();
        void SetSelectionMode(CadSelectionMode mode);
        System::String^ SelectionSummary();

        bool ScreenToPlane(int x, int y,
                           double ox, double oy, double oz,
                           double nx, double ny, double nz,
                           [System::Runtime::InteropServices::Out] CadPoint3D% worldPoint);
        double PixelToWorldLength(int pixels);

        void ClearSketch();
        void ClearPreview();
        void AddSketchLine(double x1, double y1, double z1,
                           double x2, double y2, double z2,
                           bool construction);
        void AddSketchCircle(double cx, double cy, double cz,
                             double nx, double ny, double nz,
                             double ux, double uy, double uz,
                             double radius,
                             bool construction);
        void AddSketchPoint(double x, double y, double z, bool origin);

        void AddStyledSketchLine(double x1, double y1, double z1,
                                 double x2, double y2, double z2,
                                 int visualState);
        void AddStyledSketchCircle(double cx, double cy, double cz,
                                   double nx, double ny, double nz,
                                   double ux, double uy, double uz,
                                   double radius,
                                   int visualState);
        void AddStyledSketchPoint(double x, double y, double z,
                                  int visualState);
        void AddSketchText(double x, double y, double z,
                           System::String^ text,
                           int visualState);

        void AddPreviewLine(double x1, double y1, double z1,
                            double x2, double y2, double z2);
        void AddPreviewCircle(double cx, double cy, double cz,
                              double nx, double ny, double nz,
                              double ux, double uy, double uz,
                              double radius);

        bool PreviewExtrudeProfile(cli::array<double>^ outerUvPairs,
                                   cli::array<double>^ circleHoles,
                                   double ox, double oy, double oz,
                                   double ux, double uy, double uz,
                                   double vx, double vy, double vz,
                                   double nx, double ny, double nz,
                                   double depth);
        bool PreviewExtrudePolyline(cli::array<double>^ uvPairs,
                                    double ox, double oy, double oz,
                                    double ux, double uy, double uz,
                                    double vx, double vy, double vz,
                                    double nx, double ny, double nz,
                                    double depth);
        bool PreviewExtrudeCircle(double centerU, double centerV, double radius,
                                  double ox, double oy, double oz,
                                  double ux, double uy, double uz,
                                  double vx, double vy, double vz,
                                  double nx, double ny, double nz,
                                  double depth);
        bool CommitExtrusion();
        void CancelExtrusionPreview();
        void ClearModel();

    private:
        OcctViewportState* myState;
        void ReleaseNative();
    };
}
