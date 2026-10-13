#pragma once
namespace SrcCad::Kernel::Occt
{
    // Detached immutable B-Rep values. No HWND/viewer or document mutation.
    public ref class OcctFeatureKernel abstract sealed
    {
    public:
        static System::String^ Extrude(array<double>^ outer, array<double>^ holes,
            array<double>^ circleHoles, array<double>^ circle, array<double>^ frame);
        static System::String^ Cut(System::String^ baseBody, array<double>^ outer,
            array<double>^ holes, array<double>^ circleHoles, array<double>^ circle,
            array<double>^ frame, bool throughAll);
        static System::String^ Fillet(System::String^ baseBody, array<System::String^>^ edgeReferences, double radius);
        static System::String^ Chamfer(System::String^ baseBody, array<System::String^>^ edgeReferences, double distance);
        static System::String^ Hole(System::String^ baseBody, System::String^ faceReference,
            double centerX, double centerY, double centerZ, double diameter, double depth, bool throughAll, bool reverse);
        static System::String^ Revolve(System::String^ baseBody, array<double>^ outer, array<double>^ holes,
            array<double>^ circleHoles, array<double>^ circle, array<double>^ frame,
            array<double>^ axis, double angleDegrees, bool reverse);
        static System::String^ RevolveCut(System::String^ baseBody, array<double>^ outer, array<double>^ holes,
            array<double>^ circleHoles, array<double>^ circle, array<double>^ frame,
            array<double>^ axis, double angleDegrees, bool reverse);
        static System::String^ Sweep(System::String^ baseBody, array<double>^ outer, array<double>^ circle,
            array<double>^ frame, array<double>^ pathPoints);
        static System::String^ SweepCut(System::String^ baseBody, array<double>^ outer, array<double>^ circle,
            array<double>^ frame, array<double>^ pathPoints);
        static System::String^ Loft(System::String^ baseBody, array<array<double>^>^ outers,
            array<array<double>^>^ circles, array<array<double>^>^ frames);
        static System::String^ LoftCut(System::String^ baseBody, array<array<double>^>^ outers,
            array<array<double>^>^ circles, array<array<double>^>^ frames);
        static System::String^ Shell(System::String^ baseBody, array<System::String^>^ faceReferences,
            double thickness, bool outward);
        static System::String^ Draft(System::String^ baseBody, array<System::String^>^ faceReferences,
            array<double>^ neutralPlane, double angleDegrees, bool reverse);
        static System::String^ Rib(System::String^ baseBody, array<double>^ lineUvPairs,
            array<double>^ planeFrame, double thickness, double depth, bool reverse);
        static System::String^ Mirror(System::String^ baseBody, array<double>^ planeFrame, bool keepOriginal);
        static System::String^ LinearPattern(System::String^ baseBody, array<double>^ direction, double spacing, int count);
        static System::String^ CircularPattern(System::String^ baseBody, array<double>^ axis, double angleDegrees, int count);
        static System::String^ EdgeFlange(System::String^ baseBody, array<System::String^>^ edgeReferences,
            double length, double angleDegrees, double thickness, double bendRadius, bool reverse);
        static System::String^ Transform(System::String^ body, array<double>^ transform6);
        static double CommonVolume(System::String^ bodyA, array<double>^ transformA,
            System::String^ bodyB, array<double>^ transformB);
        static array<System::String^>^ EdgeReferences(System::String^ body);
        static array<System::String^>^ FaceReferences(System::String^ body);
        // volume, solid count, face count, edge count
        static array<double>^ Inspect(System::String^ body);
    };
}
