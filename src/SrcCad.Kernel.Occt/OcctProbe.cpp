#include "OcctProbe.h"
#include <Standard_Version.hxx>
#include <Standard_Failure.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <TopoDS_Shape.hxx>

using namespace System;

namespace SrcCad::Kernel::Occt
{
    String^ OcctProbe::Version()
    {
        return gcnew String(OCC_VERSION_COMPLETE);
    }

    bool OcctProbe::CanCreateBox(double x, double y, double z)
    {
        if (x <= 0.0 || y <= 0.0 || z <= 0.0) return false;
        try
        {
            BRepPrimAPI_MakeBox maker(x, y, z);
            TopoDS_Shape shape = maker.Shape();
            return !shape.IsNull();
        }
        catch (const Standard_Failure&)
        {
            return false;
        }
    }

    String^ OcctProbe::BuildInfo()
    {
        return String::Format("Open CASCADE {0} | C++/CLI .NET 8 | x64", Version());
    }
}
