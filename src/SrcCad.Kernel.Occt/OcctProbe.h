#pragma once
namespace SrcCad::Kernel::Occt
{
    public ref class OcctProbe sealed
    {
    public:
        static System::String^ Version();
        static bool CanCreateBox(double x, double y, double z);
        static System::String^ BuildInfo();
    };
}
