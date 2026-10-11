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
        // volume, solid count, face count, edge count
        static array<double>^ Inspect(System::String^ body);
    };
}
