#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <vector>

#include <Aspect_DisplayConnection.hxx>
#include <WNT_Window.hxx>
#include <OpenGl_GraphicDriver.hxx>
#include <V3d_Viewer.hxx>
#include <V3d_View.hxx>
#include <Graphic3d_Camera.hxx>
#include <AIS_InteractiveContext.hxx>
#include <AIS_InteractiveObject.hxx>
#include <AIS_Shape.hxx>
#include <AIS_TextLabel.hxx>
#include <AIS_DisplayMode.hxx>
#include <AIS_SelectionScheme.hxx>
#include <Quantity_Color.hxx>
#include <Quantity_NameOfColor.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Edge.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Surface.hxx>
#include <Geom_Plane.hxx>
#include <Geom_RectangularTrimmedSurface.hxx>
#include <BRep_Tool.hxx>
#include <gp_Ax1.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <gp_Circ.hxx>
#include <gp_Dir.hxx>
#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#include <gp_Trsf.hxx>
#include <Standard_Failure.hxx>
#include <StdSelect_BRepOwner.hxx>
#include <Prs3d_Drawer.hxx>
#include <Prs3d_LineAspect.hxx>
#include <Prs3d_TypeOfHighlight.hxx>
#include <TCollection_ExtendedString.hxx>
#include <vcclr.h>

#include "OcctViewportBridge.h"
#include "OcctFeatureKernel.h"
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepFilletAPI_MakeFillet.hxx>
#include <BRepFilletAPI_MakeChamfer.hxx>
#include <BRepOffsetAPI_MakeThickSolid.hxx>
#include <TopTools_ListOfShape.hxx>
#include <BRepTools.hxx>
#include <BRep_Builder.hxx>
#include <BRepBndLib.hxx>
#include <Bnd_Box.hxx>
#include <TopExp.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <sstream>
#include <iomanip>
#include <limits>
#include <set>
#include <msclr/marshal_cppstd.h>

using namespace System;

namespace SrcCad::Kernel::Occt
{
    class OcctViewportState
    {
    public:
        Handle(OpenGl_GraphicDriver) Driver;
        Handle(V3d_Viewer) Viewer;
        Handle(V3d_View) View;
        Handle(AIS_InteractiveContext) Context;
        Handle(WNT_Window) Window;
        std::vector<Handle(AIS_Shape)> SketchObjects;
        std::vector<Handle(AIS_Shape)> PreviewObjects;
        std::vector<Handle(AIS_InteractiveObject)> SketchTextObjects;
        Handle(AIS_Shape) PreviewSolid;
        Handle(AIS_Shape) SolidPresentation;
        TopoDS_Shape PreviewSolidShape;
        TopoDS_Shape SolidShape;
        CadSelectionMode SelectionMode = CadSelectionMode::Face;
        CadDisplayStyle DisplayStyle = CadDisplayStyle::ShadedWithEdges;
        bool Initialized = false;
    };

    static Quantity_Color rgb(double r, double g, double b)
    {
        return Quantity_Color(r, g, b, Quantity_TOC_RGB);
    }

    static gp_Pnt planePoint(double u, double v,
                             double ox, double oy, double oz,
                             double ux, double uy, double uz,
                             double vx, double vy, double vz)
    {
        return gp_Pnt(ox + ux * u + vx * v,
                      oy + uy * u + vy * v,
                      oz + uz * u + vz * v);
    }

    struct EdgeSemanticSignature
    {
        double Length=0.0;
        double X=0.0;
        double Y=0.0;
        double Z=0.0;
    };

    static EdgeSemanticSignature edgeSemanticSignature(const TopoDS_Edge& edge)
    {
        GProp_GProps props;
        BRepGProp::LinearProperties(edge,props);
        gp_Pnt center=props.CentreOfMass();
        return {props.Mass(),center.X(),center.Y(),center.Z()};
    }

    static std::string encodeEdgeReference(const TopoDS_Edge& edge)
    {
        auto signature=edgeSemanticSignature(edge);
        std::ostringstream stream;
        stream<<std::setprecision(17)<<"E;"<<signature.Length<<";"<<signature.X<<";"<<signature.Y<<";"<<signature.Z;
        return stream.str();
    }

    static bool decodeEdgeReference(const std::string& text,EdgeSemanticSignature& signature)
    {
        if(text.rfind("E;",0)!=0)return false;
        std::string payload=text.substr(2);
        std::replace(payload.begin(),payload.end(),';',' ');
        std::istringstream stream(payload);
        return (stream>>signature.Length>>signature.X>>signature.Y>>signature.Z) &&
               std::isfinite(signature.Length)&&signature.Length>1e-12&&
               std::isfinite(signature.X)&&std::isfinite(signature.Y)&&std::isfinite(signature.Z);
    }

    static std::vector<TopoDS_Edge> resolveEdgeReferences(const TopoDS_Shape& shape,array<String^>^ references)
    {
        if(references==nullptr||references->Length==0)throw gcnew ArgumentException("At least one edge reference is required.");
        TopTools_IndexedMapOfShape edgeMap;
        TopExp::MapShapes(shape,TopAbs_EDGE,edgeMap);
        if(edgeMap.Extent()==0)throw gcnew InvalidOperationException("Base body has no edges.");

        std::set<int> used;
        std::vector<TopoDS_Edge> resolved;
        for each(String^ managed in references)
        {
            EdgeSemanticSignature wanted;
            if(String::IsNullOrWhiteSpace(managed)||
               !decodeEdgeReference(msclr::interop::marshal_as<std::string>(managed),wanted))
                throw gcnew ArgumentException("Invalid persistent edge reference.");

            int bestIndex=0;
            double bestScore=std::numeric_limits<double>::infinity();
            for(int i=1;i<=edgeMap.Extent();++i)
            {
                if(used.contains(i))continue;
                TopoDS_Edge edge=TopoDS::Edge(edgeMap(i));
                auto current=edgeSemanticSignature(edge);
                if(current.Length<=1e-12)continue;
                const double lengthScore=std::abs(std::log(current.Length/wanted.Length));
                gp_Pnt currentCenter(current.X,current.Y,current.Z);
                gp_Pnt wantedCenter(wanted.X,wanted.Y,wanted.Z);
                const double centerScore=currentCenter.Distance(wantedCenter)/std::max(1.0,wanted.Length);
                const double score=lengthScore+centerScore;
                if(score<bestScore){bestScore=score;bestIndex=i;}
            }
            if(bestIndex==0)throw gcnew InvalidOperationException("Referenced edge can no longer be resolved.");
            used.insert(bestIndex);
            resolved.push_back(TopoDS::Edge(edgeMap(bestIndex)));
        }
        return resolved;
    }

    static bool planarFaceFrame(const TopoDS_Face& face,gp_Pnt& origin,gp_Dir& u,gp_Dir& v,gp_Dir& normal)
    {
        Handle(Geom_Surface) surface=BRep_Tool::Surface(face);
        Handle(Geom_RectangularTrimmedSurface) trimmed=Handle(Geom_RectangularTrimmedSurface)::DownCast(surface);
        if(!trimmed.IsNull())surface=trimmed->BasisSurface();
        Handle(Geom_Plane) geometry=Handle(Geom_Plane)::DownCast(surface);
        if(geometry.IsNull())return false;

        gp_Ax3 axes=geometry->Pln().Position();
        origin=axes.Location();
        normal=axes.Direction();
        if(face.Orientation()==TopAbs_REVERSED)normal.Reverse();

        u=axes.XDirection();
        double bestLength=0.0;
        TopTools_IndexedMapOfShape edgeMap;
        TopExp::MapShapes(face,TopAbs_EDGE,edgeMap);
        for(int i=1;i<=edgeMap.Extent();++i)
        {
            TopoDS_Edge edge=TopoDS::Edge(edgeMap(i));
            TopoDS_Vertex a,b;
            TopExp::Vertices(edge,a,b);
            if(a.IsNull()||b.IsNull())continue;
            gp_Pnt pa=BRep_Tool::Pnt(a),pb=BRep_Tool::Pnt(b);
            const double length=pa.Distance(pb);
            if(length<=bestLength+1e-9)continue;
            gp_Vec chord(pa,pb);
            if(chord.Magnitude()<=1e-9)continue;
            bestLength=length;
            u=gp_Dir(chord);
        }
        const double ax=std::abs(u.X()),ay=std::abs(u.Y()),az=std::abs(u.Z());
        if((ax>=ay&&ax>=az&&u.X()<0.0)||(ay>ax&&ay>=az&&u.Y()<0.0)||(az>ax&&az>ay&&u.Z()<0.0))u.Reverse();
        gp_Vec nVec(normal.X(),normal.Y(),normal.Z()),uVec(u.X(),u.Y(),u.Z());
        v=gp_Dir(nVec.Crossed(uVec));
        return true;
    }

    struct FaceSemanticSignature
    {
        double Area=0.0;
        double X=0.0,Y=0.0,Z=0.0;
        double NX=0.0,NY=0.0,NZ=0.0;
    };

    static bool faceSemanticSignature(const TopoDS_Face& face,FaceSemanticSignature& signature)
    {
        gp_Pnt origin;gp_Dir u,v,normal;
        if(!planarFaceFrame(face,origin,u,v,normal))return false;
        GProp_GProps props;BRepGProp::SurfaceProperties(face,props);
        gp_Pnt center=props.CentreOfMass();
        signature={props.Mass(),center.X(),center.Y(),center.Z(),normal.X(),normal.Y(),normal.Z()};
        return std::isfinite(signature.Area)&&signature.Area>1e-12;
    }

    static std::string encodeFaceReference(const TopoDS_Face& face)
    {
        FaceSemanticSignature signature;
        if(!faceSemanticSignature(face,signature))return {};
        std::ostringstream stream;
        stream<<std::setprecision(17)<<"F;"<<signature.Area<<";"<<signature.X<<";"<<signature.Y<<";"<<signature.Z
              <<";"<<signature.NX<<";"<<signature.NY<<";"<<signature.NZ;
        return stream.str();
    }

    static bool decodeFaceReference(const std::string& text,FaceSemanticSignature& signature)
    {
        if(text.rfind("F;",0)!=0)return false;
        std::string payload=text.substr(2);
        std::replace(payload.begin(),payload.end(),';',' ');
        std::istringstream stream(payload);
        return (stream>>signature.Area>>signature.X>>signature.Y>>signature.Z>>signature.NX>>signature.NY>>signature.NZ) &&
               std::isfinite(signature.Area)&&signature.Area>1e-12&&
               std::isfinite(signature.X)&&std::isfinite(signature.Y)&&std::isfinite(signature.Z)&&
               std::isfinite(signature.NX)&&std::isfinite(signature.NY)&&std::isfinite(signature.NZ);
    }

    static TopoDS_Face resolveFaceReference(const TopoDS_Shape& shape,String^ managed)
    {
        if(String::IsNullOrWhiteSpace(managed))throw gcnew ArgumentException("Face reference is required.");
        FaceSemanticSignature wanted;
        if(!decodeFaceReference(msclr::interop::marshal_as<std::string>(managed),wanted))
            throw gcnew ArgumentException("Invalid persistent face reference.");

        TopTools_IndexedMapOfShape faces;
        TopExp::MapShapes(shape,TopAbs_FACE,faces);
        int bestIndex=0;double bestScore=std::numeric_limits<double>::infinity();
        gp_Dir wantedNormal(wanted.NX,wanted.NY,wanted.NZ);
        for(int i=1;i<=faces.Extent();++i)
        {
            TopoDS_Face face=TopoDS::Face(faces(i));
            FaceSemanticSignature current;
            if(!faceSemanticSignature(face,current))continue;
            const double areaScore=std::abs(std::log(current.Area/wanted.Area));
            gp_Pnt a(current.X,current.Y,current.Z),b(wanted.X,wanted.Y,wanted.Z);
            const double centerScore=a.Distance(b)/std::max(1.0,std::sqrt(wanted.Area));
            gp_Dir currentNormal(current.NX,current.NY,current.NZ);
            const double normalScore=(1.0-currentNormal.Dot(wantedNormal))*4.0;
            const double score=areaScore+centerScore+normalScore;
            if(score<bestScore){bestScore=score;bestIndex=i;}
        }
        if(bestIndex==0)throw gcnew InvalidOperationException("Referenced planar face can no longer be resolved.");
        return TopoDS::Face(faces(bestIndex));
    }

    static void eraseList(OcctViewportState* state, std::vector<Handle(AIS_Shape)>& list)
    {
        if (state == nullptr || state->Context.IsNull())
        {
            list.clear();
            return;
        }
        for (auto& item : list)
        {
            if (!item.IsNull()) state->Context->Remove(item, false);
        }
        list.clear();
    }

    static void eraseInteractiveList(OcctViewportState* state,
                                     std::vector<Handle(AIS_InteractiveObject)>& list)
    {
        if (state == nullptr || state->Context.IsNull())
        {
            list.clear();
            return;
        }
        for (auto& item : list)
        {
            if (!item.IsNull()) state->Context->Remove(item, false);
        }
        list.clear();
    }

    static Quantity_Color sketchColor(int visualState)
    {
        switch (visualState)
        {
            case 1: return rgb(0.45, 0.45, 0.45); // construction
            case 2: return rgb(1.00, 0.45, 0.00); // selected
            case 3: return rgb(0.08, 0.08, 0.08); // fully defined
            case 4: return rgb(0.90, 0.10, 0.10); // conflicting
            case 5: return rgb(0.82, 0.16, 0.12); // dimension
            case 6: return rgb(0.95, 0.55, 0.05); // point
            case 7: return rgb(0.90, 0.10, 0.10); // origin
            default: return rgb(0.05, 0.45, 0.95); // under defined
        }
    }

    OcctViewportBridge::OcctViewportBridge()
        : myState(new OcctViewportState())
    {
    }

    OcctViewportBridge::~OcctViewportBridge()
    {
        ReleaseNative();
        GC::SuppressFinalize(this);
    }

    OcctViewportBridge::!OcctViewportBridge()
    {
        ReleaseNative();
    }

    void OcctViewportBridge::ReleaseNative()
    {
        if (myState != nullptr)
        {
            delete myState;
            myState = nullptr;
        }
    }

    bool OcctViewportBridge::Init(IntPtr windowHandle)
    {
        if (myState == nullptr || windowHandle == IntPtr::Zero) return false;
        try
        {
            Handle(Aspect_DisplayConnection) displayConnection = new Aspect_DisplayConnection();
            myState->Driver = new OpenGl_GraphicDriver(displayConnection);
            myState->Viewer = new V3d_Viewer(myState->Driver);
            myState->Viewer->SetDefaultLights();
            myState->Viewer->SetLightOn();
            // Neutral CAD background with enough contrast for light-gray parts.
            myState->Viewer->SetDefaultBackgroundColor(rgb(0.82, 0.87, 0.91));

            myState->View = myState->Viewer->CreateView();
            myState->Window = new WNT_Window(reinterpret_cast<HWND>(windowHandle.ToPointer()));
            myState->View->SetWindow(myState->Window);
            if (!myState->Window->IsMapped()) myState->Window->Map();

            myState->View->Camera()->SetProjectionType(Graphic3d_Camera::Projection_Orthographic);
            myState->Context = new AIS_InteractiveContext(myState->Viewer);
            myState->Context->SetToHilightSelected(true);
            myState->Context->SetAutoActivateSelection(true);
            myState->Context->HighlightStyle(Prs3d_TypeOfHighlight_Dynamic)->SetColor(rgb(1.00, 0.82, 0.10));
            myState->Context->HighlightStyle(Prs3d_TypeOfHighlight_LocalDynamic)->SetColor(rgb(1.00, 0.82, 0.10));
            myState->Context->HighlightStyle(Prs3d_TypeOfHighlight_Selected)->SetColor(rgb(1.00, 0.45, 0.00));
            myState->Context->HighlightStyle(Prs3d_TypeOfHighlight_LocalSelected)->SetColor(rgb(1.00, 0.45, 0.00));
            myState->Initialized = true;
            SetIsoView();
            myState->View->MustBeResized();
            myState->View->Redraw();
            return true;
        }
        catch (const Standard_Failure&)
        {
            myState->Initialized = false;
            return false;
        }
        catch (...)
        {
            myState->Initialized = false;
            return false;
        }
    }

    bool OcctViewportBridge::IsInitialized()
    {
        return myState != nullptr && myState->Initialized;
    }

    void OcctViewportBridge::Resize()
    {
        if (!IsInitialized()) return;
        myState->View->MustBeResized();
        myState->View->Redraw();
    }

    void OcctViewportBridge::Redraw()
    {
        if (!IsInitialized()) return;
        myState->View->Redraw();
    }

    void OcctViewportBridge::FitAll()
    {
        if (!IsInitialized()) return;
        myState->View->FitAll(0.01, false);
        myState->View->ZFitAll();
        myState->View->Redraw();
    }

    void OcctViewportBridge::SetOrthographic(bool enabled)
    {
        if (!IsInitialized()) return;
        myState->View->Camera()->SetProjectionType(
            enabled ? Graphic3d_Camera::Projection_Orthographic
                    : Graphic3d_Camera::Projection_Perspective);
        myState->View->Redraw();
    }

    void OcctViewportBridge::SetNormalView(double nx, double ny, double nz,
                                           double upx, double upy, double upz)
    {
        if (!IsInitialized()) return;
        try
        {
            gp_Dir direction(-nx, -ny, -nz); // Camera looks toward the plane.
            gp_Dir up(upx, upy, upz);
            myState->View->Camera()->SetDirection(direction);
            myState->View->Camera()->SetUp(up);
            FitAll();
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::SetFrontView() { SetNormalView(0, 0, 1, 0, 1, 0); }
    void OcctViewportBridge::SetTopView()   { SetNormalView(0, 1, 0, 0, 0, -1); }
    void OcctViewportBridge::SetRightView() { SetNormalView(1, 0, 0, 0, 1, 0); }

    void OcctViewportBridge::SetIsoView()
    {
        if (!IsInitialized()) return;
        try
        {
            myState->View->Camera()->SetDirection(gp_Dir(-1, -1, -1));
            myState->View->Camera()->SetUp(gp_Dir(-1, 2, -1));
            FitAll();
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::StartRotation(int x, int y)
    {
        if (IsInitialized()) myState->View->StartRotation(x, y);
    }

    void OcctViewportBridge::Rotate(int x, int y)
    {
        if (IsInitialized()) myState->View->Rotation(x, y);
    }

    void OcctViewportBridge::Pan(int dx, int dy)
    {
        if (!IsInitialized()) return;
        myState->View->Pan(dx, dy);
        myState->View->Redraw();
    }

    void OcctViewportBridge::ZoomWheel(int delta)
    {
        if (!IsInitialized() || delta == 0) return;
        const double factor = delta > 0 ? 1.12 : (1.0 / 1.12);
        const double current = myState->View->Scale();
        myState->View->SetScale(std::max(1e-8, current * factor));
        myState->View->Redraw();
    }

    void OcctViewportBridge::MoveTo(int x, int y)
    {
        if (!IsInitialized()) return;
        myState->Context->MoveTo(x, y, myState->View, true);
    }

    bool OcctViewportBridge::SelectDetected(bool toggle)
    {
        if (!IsInitialized()) return false;
        if (!myState->Context->HasDetected())
        {
            if (!toggle)
            {
                myState->Context->ClearSelected(false);
                myState->Context->UpdateCurrentViewer();
            }
            return false;
        }

        myState->Context->SelectDetected(
            toggle ? AIS_SelectionScheme_XOR : AIS_SelectionScheme_Replace);
        myState->Context->UpdateCurrentViewer();
        return myState->Context->NbSelected() > 0;
    }

    void OcctViewportBridge::ClearSelection()
    {
        if (!IsInitialized()) return;
        myState->Context->ClearSelected(false);
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::SetSelectionMode(CadSelectionMode mode)
    {
        if (!IsInitialized()) return;
        myState->SelectionMode = mode;

        int selectionMode = 0;
        switch (mode)
        {
            case CadSelectionMode::Vertex: selectionMode = AIS_Shape::SelectionMode(TopAbs_VERTEX); break;
            case CadSelectionMode::Edge:   selectionMode = AIS_Shape::SelectionMode(TopAbs_EDGE);   break;
            case CadSelectionMode::Face:   selectionMode = AIS_Shape::SelectionMode(TopAbs_FACE);   break;
            default: selectionMode = 0; break;
        }

        if (!myState->SolidPresentation.IsNull())
        {
            myState->Context->Deactivate(myState->SolidPresentation);
            myState->Context->Activate(myState->SolidPresentation, selectionMode, true);
        }
        myState->Context->UpdateCurrentViewer();
    }

    String^ OcctViewportBridge::SelectionSummary()
    {
        if (!IsInitialized()) return "Seçim yok";
        const int count = myState->Context->NbSelected();
        if (count <= 0) return "Seçim yok";

        String^ mode = "Obje";
        switch (myState->SelectionMode)
        {
            case CadSelectionMode::Vertex: mode = "Köşe"; break;
            case CadSelectionMode::Edge: mode = "Kenar"; break;
            case CadSelectionMode::Face: mode = "Yüz"; break;
            default: break;
        }
        return String::Format("{0}: {1} seçili", mode, count);
    }

    array<String^>^ OcctViewportBridge::SelectedEdgeReferences()
    {
        if(!IsInitialized())return gcnew array<String^>(0);
        auto values=gcnew System::Collections::Generic::List<String^>();
        for(myState->Context->InitSelected();myState->Context->MoreSelected();myState->Context->NextSelected())
        {
            if(!myState->Context->HasSelectedShape())continue;
            TopoDS_Shape selected=myState->Context->SelectedShape();
            if(selected.IsNull()||selected.ShapeType()!=TopAbs_EDGE)continue;
            auto value=encodeEdgeReference(TopoDS::Edge(selected));
            values->Add(gcnew String(value.c_str()));
        }
        return values->ToArray();
    }

    String^ OcctViewportBridge::SelectedFaceReference()
    {
        if(!IsInitialized()||myState->Context->NbSelected()!=1)return nullptr;
        try
        {
            myState->Context->InitSelected();
            if(!myState->Context->MoreSelected()||!myState->Context->HasSelectedShape())return nullptr;
            TopoDS_Shape selected=myState->Context->SelectedShape();
            if(selected.IsNull()||selected.ShapeType()!=TopAbs_FACE)return nullptr;
            auto value=encodeFaceReference(TopoDS::Face(selected));
            return value.empty()?nullptr:gcnew String(value.c_str());
        }
        catch(const Standard_Failure&){return nullptr;}
    }

    array<String^>^ OcctViewportBridge::SelectedFaceReferences()
    {
        if(!IsInitialized())return gcnew array<String^>(0);
        auto values=gcnew System::Collections::Generic::List<String^>();
        for(myState->Context->InitSelected();myState->Context->MoreSelected();myState->Context->NextSelected())
        {
            if(!myState->Context->HasSelectedShape())continue;
            TopoDS_Shape selected=myState->Context->SelectedShape();
            if(selected.IsNull()||selected.ShapeType()!=TopAbs_FACE)continue;
            auto value=encodeFaceReference(TopoDS::Face(selected));
            if(!value.empty())values->Add(gcnew String(value.c_str()));
        }
        return values->ToArray();
    }

    array<double>^ OcctViewportBridge::SelectedTopologyMeasure()
    {
        if(!IsInitialized()||myState->Context->NbSelected()!=1)return gcnew array<double>(0);
        try
        {
            myState->Context->InitSelected();
            if(!myState->Context->MoreSelected()||!myState->Context->HasSelectedShape())return gcnew array<double>(0);
            TopoDS_Shape selected=myState->Context->SelectedShape();
            if(selected.IsNull())return gcnew array<double>(0);
            GProp_GProps props;
            if(selected.ShapeType()==TopAbs_EDGE)
            {
                BRepGProp::LinearProperties(selected,props);
                return gcnew array<double>{2.0,props.Mass(),props.CentreOfMass().X(),props.CentreOfMass().Y(),props.CentreOfMass().Z()};
            }
            if(selected.ShapeType()==TopAbs_FACE)
            {
                BRepGProp::SurfaceProperties(selected,props);
                return gcnew array<double>{4.0,props.Mass(),props.CentreOfMass().X(),props.CentreOfMass().Y(),props.CentreOfMass().Z()};
            }
            if(selected.ShapeType()==TopAbs_VERTEX)
            {
                gp_Pnt p=BRep_Tool::Pnt(TopoDS::Vertex(selected));
                return gcnew array<double>{1.0,0.0,p.X(),p.Y(),p.Z()};
            }
            return gcnew array<double>(0);
        }
        catch(const Standard_Failure&){return gcnew array<double>(0);}
    }

    bool OcctViewportBridge::TryGetSelectedPlanarFace(CadPlane3D% plane)
    {
        if (!IsInitialized() || myState->Context->NbSelected() != 1) return false;
        try
        {
            myState->Context->InitSelected();
            if (!myState->Context->MoreSelected() || !myState->Context->HasSelectedShape()) return false;
            TopoDS_Shape selected = myState->Context->SelectedShape();
            if (selected.IsNull() || selected.ShapeType() != TopAbs_FACE) return false;
            gp_Pnt origin;gp_Dir u,v,normal;
            if(!planarFaceFrame(TopoDS::Face(selected),origin,u,v,normal))return false;
            plane.Origin.X=origin.X(); plane.Origin.Y=origin.Y(); plane.Origin.Z=origin.Z();
            plane.U.X=u.X(); plane.U.Y=u.Y(); plane.U.Z=u.Z();
            plane.V.X=v.X(); plane.V.Y=v.Y(); plane.V.Z=v.Z();
            plane.Normal.X=normal.X(); plane.Normal.Y=normal.Y(); plane.Normal.Z=normal.Z();
            return true;
        }
        catch (const Standard_Failure&) { return false; }
    }

    void OcctViewportBridge::SetCommittedSolidTransparency(double transparency)
    {
        if (!IsInitialized() || myState->SolidPresentation.IsNull()) return;
        transparency=std::clamp(transparency,0.0,0.80);
        myState->Context->SetTransparency(myState->SolidPresentation,transparency,false);
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::SetDisplayStyle(CadDisplayStyle style)
    {
        if (!IsInitialized()) return;
        myState->DisplayStyle=style;
        if (myState->SolidPresentation.IsNull()) return;

        Handle(Prs3d_Drawer) drawer=myState->SolidPresentation->Attributes();
        drawer->SetFaceBoundaryDraw(style==CadDisplayStyle::ShadedWithEdges);
        if (style==CadDisplayStyle::ShadedWithEdges)
        {
            drawer->SetupOwnFaceBoundaryAspect();
            if (!drawer->FaceBoundaryAspect().IsNull())
            {
                drawer->FaceBoundaryAspect()->SetColor(rgb(0.16,0.19,0.22));
                drawer->FaceBoundaryAspect()->SetWidth(1.15);
            }
        }

        const int mode=style==CadDisplayStyle::Wireframe?AIS_WireFrame:AIS_Shaded;
        myState->Context->SetDisplayMode(myState->SolidPresentation,mode,false);
        myState->Context->Redisplay(myState->SolidPresentation,false);
        myState->Context->UpdateCurrentViewer();
    }

    bool OcctViewportBridge::ScreenToPlane(int x, int y,
                                           double ox, double oy, double oz,
                                           double nx, double ny, double nz,
                                           CadPoint3D% worldPoint)
    {
        if (!IsInitialized()) return false;
        double px = 0, py = 0, pz = 0, vx = 0, vy = 0, vz = 0;
        myState->View->ConvertWithProj(x, y, px, py, pz, vx, vy, vz);
        const double denom = nx * vx + ny * vy + nz * vz;
        if (std::abs(denom) < 1e-12) return false;
        const double t = (nx * (ox - px) + ny * (oy - py) + nz * (oz - pz)) / denom;
        worldPoint.X = px + vx * t;
        worldPoint.Y = py + vy * t;
        worldPoint.Z = pz + vz * t;
        return true;
    }

    double OcctViewportBridge::PixelToWorldLength(int pixels)
    {
        if (!IsInitialized()) return 1.0;
        return std::abs(myState->View->Convert(std::max(1, pixels)));
    }

    void OcctViewportBridge::ClearSketch()
    {
        if (!IsInitialized()) return;
        eraseList(myState, myState->SketchObjects);
        eraseInteractiveList(myState, myState->SketchTextObjects);
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::ClearPreview()
    {
        if (!IsInitialized()) return;
        eraseList(myState, myState->PreviewObjects);
        myState->Context->UpdateCurrentViewer();
    }

    static Handle(AIS_Shape) displayShape(OcctViewportState* state,
                                          const TopoDS_Shape& shape,
                                          const Quantity_Color& color,
                                          int displayMode,
                                          bool selectable,
                                          double transparency = 0.0)
    {
        Handle(AIS_Shape) ais = new AIS_Shape(shape);
        int effectiveMode=displayMode;
        if (displayMode == AIS_Shaded)
        {
            const bool boundaries=!selectable || state->DisplayStyle==CadDisplayStyle::ShadedWithEdges;
            if (selectable && state->DisplayStyle==CadDisplayStyle::Wireframe) effectiveMode=AIS_WireFrame;
            Handle(Prs3d_Drawer) drawer=ais->Attributes();
            drawer->SetFaceBoundaryDraw(boundaries);
            if (boundaries)
            {
                drawer->SetupOwnFaceBoundaryAspect();
                if (!drawer->FaceBoundaryAspect().IsNull())
                {
                    drawer->FaceBoundaryAspect()->SetColor(rgb(0.16,0.19,0.22));
                    drawer->FaceBoundaryAspect()->SetWidth(1.15);
                }
            }
        }
        state->Context->Display(ais, effectiveMode, selectable ? 0 : -1, false);
        state->Context->SetColor(ais, color, false);
        if (transparency > 0.0) state->Context->SetTransparency(ais, transparency, false);
        return ais;
    }

    void OcctViewportBridge::AddSketchLine(double x1, double y1, double z1,
                                           double x2, double y2, double z2,
                                           bool construction)
    {
        if (!IsInitialized()) return;
        try
        {
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Pnt(x1, y1, z1), gp_Pnt(x2, y2, z2));
            if (!edgeMaker.IsDone()) return;
            TopoDS_Edge edge = edgeMaker.Edge();
            auto ais = displayShape(myState, edge,
                                    construction ? rgb(0.50, 0.50, 0.50) : rgb(0.05, 0.45, 0.95),
                                    AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
            myState->Context->UpdateCurrentViewer();
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddSketchCircle(double cx, double cy, double cz,
                                             double nx, double ny, double nz,
                                             double ux, double uy, double uz,
                                             double radius,
                                             bool construction)
    {
        if (!IsInitialized() || radius <= 0.0) return;
        try
        {
            gp_Ax2 ax(gp_Pnt(cx, cy, cz), gp_Dir(nx, ny, nz), gp_Dir(ux, uy, uz));
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Circ(ax, radius));
            if (!edgeMaker.IsDone()) return;
            TopoDS_Edge edge = edgeMaker.Edge();
            auto ais = displayShape(myState, edge,
                                    construction ? rgb(0.50, 0.50, 0.50) : rgb(0.05, 0.45, 0.95),
                                    AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
            myState->Context->UpdateCurrentViewer();
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddSketchPoint(double x, double y, double z, bool origin)
    {
        if (!IsInitialized()) return;
        try
        {
            BRepBuilderAPI_MakeVertex vertexMaker(gp_Pnt(x, y, z));
            TopoDS_Vertex vertex = vertexMaker.Vertex();
            auto ais = displayShape(myState, vertex,
                                    origin ? rgb(0.90, 0.10, 0.10) : rgb(0.95, 0.55, 0.05),
                                    AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }


    void OcctViewportBridge::AddStyledSketchLine(double x1, double y1, double z1,
                                                 double x2, double y2, double z2,
                                                 int visualState)
    {
        if (!IsInitialized()) return;
        try
        {
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Pnt(x1, y1, z1), gp_Pnt(x2, y2, z2));
            if (!edgeMaker.IsDone()) return;
            auto ais = displayShape(myState, edgeMaker.Edge(), sketchColor(visualState), AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddStyledSketchCircle(double cx, double cy, double cz,
                                                   double nx, double ny, double nz,
                                                   double ux, double uy, double uz,
                                                   double radius,
                                                   int visualState)
    {
        if (!IsInitialized() || radius <= 0.0) return;
        try
        {
            gp_Ax2 ax(gp_Pnt(cx, cy, cz), gp_Dir(nx, ny, nz), gp_Dir(ux, uy, uz));
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Circ(ax, radius));
            if (!edgeMaker.IsDone()) return;
            auto ais = displayShape(myState, edgeMaker.Edge(), sketchColor(visualState), AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddStyledSketchPoint(double x, double y, double z,
                                                  int visualState)
    {
        if (!IsInitialized()) return;
        try
        {
            BRepBuilderAPI_MakeVertex vertexMaker(gp_Pnt(x, y, z));
            auto ais = displayShape(myState, vertexMaker.Vertex(), sketchColor(visualState), AIS_WireFrame, false);
            myState->SketchObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddSketchText(double x, double y, double z,
                                           String^ text,
                                           int visualState)
    {
        if (!IsInitialized() || String::IsNullOrWhiteSpace(text)) return;
        try
        {
            pin_ptr<const wchar_t> chars = PtrToStringChars(text);
            TCollection_ExtendedString label((Standard_ExtString)chars);
            Handle(AIS_TextLabel) ais = new AIS_TextLabel();
            ais->SetText(label);
            ais->SetPosition(gp_Pnt(x, y, z));
            ais->SetColor(sketchColor(visualState));
            ais->SetHeight(14.0);
            myState->Context->Display(ais, false);
            myState->SketchTextObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddPreviewLine(double x1, double y1, double z1,
                                            double x2, double y2, double z2)
    {
        if (!IsInitialized()) return;
        try
        {
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Pnt(x1, y1, z1), gp_Pnt(x2, y2, z2));
            if (!edgeMaker.IsDone()) return;
            TopoDS_Edge edge = edgeMaker.Edge();
            auto ais = displayShape(myState, edge, rgb(0.95, 0.45, 0.05), AIS_WireFrame, false);
            myState->PreviewObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    void OcctViewportBridge::AddPreviewCircle(double cx, double cy, double cz,
                                              double nx, double ny, double nz,
                                              double ux, double uy, double uz,
                                              double radius)
    {
        if (!IsInitialized() || radius <= 0.0) return;
        try
        {
            gp_Ax2 ax(gp_Pnt(cx, cy, cz), gp_Dir(nx, ny, nz), gp_Dir(ux, uy, uz));
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Circ(ax, radius));
            if (!edgeMaker.IsDone()) return;
            TopoDS_Edge edge = edgeMaker.Edge();
            auto ais = displayShape(myState, edge, rgb(0.95, 0.45, 0.05), AIS_WireFrame, false);
            myState->PreviewObjects.push_back(ais);
        }
        catch (const Standard_Failure&)
        {
        }
    }

    static TopoDS_Shape makePrismFromProfile(array<double>^ outerUvPairs,
                                              array<double>^ innerLoopData,
                                              array<double>^ circleHoles,
                                              double ox, double oy, double oz,
                                              double ux, double uy, double uz,
                                              double vx, double vy, double vz,
                                              double nx, double ny, double nz,
                                              double depth)
    {
        if (!std::isfinite(depth) || std::abs(depth) < 1e-9 || outerUvPairs == nullptr || outerUvPairs->Length < 6 || (outerUvPairs->Length % 2) != 0)
            return TopoDS_Shape();

        BRepBuilderAPI_MakePolygon polygon;
        const int count = outerUvPairs->Length / 2;
        for (int i = 0; i < count; ++i)
        {
            const double u = outerUvPairs[i * 2];
            const double v = outerUvPairs[i * 2 + 1];
            polygon.Add(planePoint(u, v, ox, oy, oz, ux, uy, uz, vx, vy, vz));
        }
        polygon.Close();
        if (!polygon.IsDone()) return TopoDS_Shape();

        TopoDS_Wire outerWire = polygon.Wire();
        BRepBuilderAPI_MakeFace faceMaker(outerWire, true);
        if (!faceMaker.IsDone()) return TopoDS_Shape();

        if (innerLoopData != nullptr)
        {
            int cursor = 0;
            while (cursor < innerLoopData->Length)
            {
                const int count = (int)innerLoopData[cursor++];
                if (count < 3 || cursor + count * 2 > innerLoopData->Length)
                    return TopoDS_Shape();

                BRepBuilderAPI_MakePolygon holePolygon;
                for (int i = 0; i < count; ++i)
                {
                    const double u = innerLoopData[cursor++];
                    const double v = innerLoopData[cursor++];
                    holePolygon.Add(planePoint(u, v, ox, oy, oz, ux, uy, uz, vx, vy, vz));
                }
                holePolygon.Close();
                if (!holePolygon.IsDone()) return TopoDS_Shape();

                TopoDS_Wire innerWire = holePolygon.Wire();
                innerWire.Reverse();
                faceMaker.Add(innerWire);
            }
        }

        if (circleHoles != nullptr)
        {
            if ((circleHoles->Length % 3) != 0)
                return TopoDS_Shape();

            for (int i = 0; i < circleHoles->Length; i += 3)
            {
                const double centerU = circleHoles[i];
                const double centerV = circleHoles[i + 1];
                const double radius = circleHoles[i + 2];
                if (radius <= 0.0) return TopoDS_Shape();

                gp_Pnt center = planePoint(centerU, centerV,
                                           ox, oy, oz,
                                           ux, uy, uz,
                                           vx, vy, vz);
                gp_Ax2 ax(center, gp_Dir(nx, ny, nz), gp_Dir(ux, uy, uz));
                BRepBuilderAPI_MakeEdge edgeMaker(gp_Circ(ax, radius));
                if (!edgeMaker.IsDone()) return TopoDS_Shape();

                BRepBuilderAPI_MakeWire wireMaker(edgeMaker.Edge());
                if (!wireMaker.IsDone()) return TopoDS_Shape();

                TopoDS_Wire innerWire = wireMaker.Wire();
                innerWire.Reverse();
                faceMaker.Add(innerWire);
            }
        }

        if (!faceMaker.IsDone()) return TopoDS_Shape();
        gp_Vec extrusion(nx * depth, ny * depth, nz * depth);
        BRepPrimAPI_MakePrism prism(faceMaker.Face(), extrusion, true, true);
        TopoDS_Shape solid = prism.Shape();
        if (solid.IsNull() || solid.ShapeType() != TopAbs_SOLID || !BRepCheck_Analyzer(solid).IsValid())
            return TopoDS_Shape();
        return solid;
    }

    static TopoDS_Shape makePrismFromPolyline(array<double>^ uvPairs,
                                               double ox, double oy, double oz,
                                               double ux, double uy, double uz,
                                               double vx, double vy, double vz,
                                               double nx, double ny, double nz,
                                               double depth)
    {
        return makePrismFromProfile(uvPairs, nullptr, nullptr,
                                    ox, oy, oz,
                                    ux, uy, uz,
                                    vx, vy, vz,
                                    nx, ny, nz,
                                    depth);
    }

    static void validateFeatureFrame(array<double>^ frame)
    {
        if (frame == nullptr || frame->Length != 13) throw gcnew ArgumentException("Feature frame requires 13 values.");
        for each(double value in frame) if (!std::isfinite(value)) throw gcnew ArgumentException("Non-finite feature parameter.");
        if (std::abs(frame[12]) < 1e-9) throw gcnew ArgumentException("Zero extrusion depth.");
        gp_Vec u(frame[3],frame[4],frame[5]), v(frame[6],frame[7],frame[8]), n(frame[9],frame[10],frame[11]);
        if (std::abs(u.Magnitude()-1)>1e-9 || std::abs(v.Magnitude()-1)>1e-9 || std::abs(n.Magnitude()-1)>1e-9 ||
            std::abs(u.Dot(v))>1e-9 || (u.Crossed(v)-n).Magnitude()>1e-9)
            throw gcnew ArgumentException("Invalid orthonormal feature frame.");
    }

    static TopoDS_Face featureFace(array<double>^ outer,array<double>^ holes,array<double>^ circleHoles,
                                  array<double>^ circle,array<double>^ f)
    {
        validateFeatureFrame(f);
        if(circle!=nullptr&&circle->Length>0)
        {
            if(circle->Length!=3||!std::isfinite(circle[0])||!std::isfinite(circle[1])||
               !std::isfinite(circle[2])||circle[2]<=1e-9)throw gcnew ArgumentException("Invalid circle profile.");
            gp_Pnt center=planePoint(circle[0],circle[1],f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8]);
            BRepBuilderAPI_MakeEdge edge(gp_Circ(gp_Ax2(center,gp_Dir(f[9],f[10],f[11]),gp_Dir(f[3],f[4],f[5])),circle[2]));
            if(!edge.IsDone())throw gcnew InvalidOperationException("Circle profile edge failed.");
            BRepBuilderAPI_MakeWire wire(edge.Edge());
            if(!wire.IsDone())throw gcnew InvalidOperationException("Circle profile wire failed.");
            BRepBuilderAPI_MakeFace face(wire.Wire(),true);
            if(!face.IsDone())throw gcnew InvalidOperationException("Circle profile face failed.");
            return face.Face();
        }

        if(outer==nullptr||outer->Length<6||(outer->Length%2)!=0)throw gcnew ArgumentException("Invalid closed profile.");
        BRepBuilderAPI_MakePolygon polygon;
        for(int i=0;i<outer->Length;i+=2)
            polygon.Add(planePoint(outer[i],outer[i+1],f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8]));
        polygon.Close();
        if(!polygon.IsDone())throw gcnew InvalidOperationException("Profile polygon failed.");
        BRepBuilderAPI_MakeFace faceMaker(polygon.Wire(),true);
        if(!faceMaker.IsDone())throw gcnew InvalidOperationException("Profile face failed.");

        if(holes!=nullptr)
        {
            int cursor=0;
            while(cursor<holes->Length)
            {
                const int count=(int)holes[cursor++];
                if(count<3||cursor+count*2>holes->Length)throw gcnew ArgumentException("Invalid inner profile.");
                BRepBuilderAPI_MakePolygon holePolygon;
                for(int i=0;i<count;i++)
                {
                    double u=holes[cursor++],vv=holes[cursor++];
                    holePolygon.Add(planePoint(u,vv,f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8]));
                }
                holePolygon.Close();
                if(!holePolygon.IsDone())throw gcnew InvalidOperationException("Inner profile polygon failed.");
                TopoDS_Wire wire=holePolygon.Wire();wire.Reverse();faceMaker.Add(wire);
            }
        }
        if(circleHoles!=nullptr)
        {
            if((circleHoles->Length%3)!=0)throw gcnew ArgumentException("Invalid circular holes.");
            for(int i=0;i<circleHoles->Length;i+=3)
            {
                double cu=circleHoles[i],cv=circleHoles[i+1],radius=circleHoles[i+2];
                if(radius<=1e-9)throw gcnew ArgumentException("Invalid circular hole radius.");
                gp_Pnt center=planePoint(cu,cv,f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8]);
                BRepBuilderAPI_MakeEdge edge(gp_Circ(gp_Ax2(center,gp_Dir(f[9],f[10],f[11]),gp_Dir(f[3],f[4],f[5])),radius));
                BRepBuilderAPI_MakeWire wire(edge.Edge());
                TopoDS_Wire holeWire=wire.Wire();holeWire.Reverse();faceMaker.Add(holeWire);
            }
        }
        if(!faceMaker.IsDone())throw gcnew InvalidOperationException("Profile face failed.");
        return faceMaker.Face();
    }

    static TopoDS_Shape featureRevolve(array<double>^ outer,array<double>^ holes,array<double>^ circleHoles,
                                       array<double>^ circle,array<double>^ f,array<double>^ axis,
                                       double angleDegrees,bool reverse)
    {
        if(axis==nullptr||axis->Length!=6)throw gcnew ArgumentException("Revolve axis requires 6 values.");
        for each(double value in axis)if(!std::isfinite(value))throw gcnew ArgumentException("Invalid revolve axis.");
        if(!std::isfinite(angleDegrees)||angleDegrees<=1e-9||angleDegrees>360.0)
            throw gcnew ArgumentException("Revolve angle must be in (0,360].");
        gp_Pnt a(axis[0],axis[1],axis[2]),b(axis[3],axis[4],axis[5]);
        gp_Vec direction(a,b);
        if(direction.Magnitude()<=1e-9)throw gcnew ArgumentException("Zero-length revolve axis.");
        gp_Dir dir(direction);if(reverse)dir.Reverse();
        TopoDS_Face face=featureFace(outer,holes,circleHoles,circle,f);
        const double radians=angleDegrees*3.14159265358979323846/180.0;
        BRepPrimAPI_MakeRevol revol(face,gp_Ax1(a,dir),radians,true);
        TopoDS_Shape result=revol.Shape();
        if(result.IsNull()||!BRepCheck_Analyzer(result).IsValid())throw gcnew InvalidOperationException("Invalid revolved B-Rep.");
        return result;
    }

    static TopoDS_Shape featurePrism(array<double>^ outer, array<double>^ holes, array<double>^ circleHoles,
                                    array<double>^ circle, array<double>^ f)
    {
        validateFeatureFrame(f);
        if (circle != nullptr && circle->Length > 0)
        {
            if (circle->Length != 3 || !std::isfinite(circle[0]) || !std::isfinite(circle[1]) ||
                !std::isfinite(circle[2]) || circle[2] <= 1e-9) throw gcnew ArgumentException("Invalid circle profile.");
            gp_Pnt center = planePoint(circle[0],circle[1],f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8]);
            BRepBuilderAPI_MakeEdge edge(gp_Circ(gp_Ax2(center,gp_Dir(f[9],f[10],f[11]),gp_Dir(f[3],f[4],f[5])),circle[2]));
            BRepBuilderAPI_MakeWire wire(edge.Edge());
            BRepBuilderAPI_MakeFace face(wire.Wire(),true);
            BRepPrimAPI_MakePrism prism(face.Face(),gp_Vec(f[9]*f[12],f[10]*f[12],f[11]*f[12]),true,true);
            return prism.Shape();
        }
        return makePrismFromProfile(outer,holes,circleHoles,f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12]);
    }
    static double featureVolume(const TopoDS_Shape& shape)
    {
        if (shape.IsNull() || !BRepCheck_Analyzer(shape).IsValid()) throw gcnew InvalidOperationException("Invalid feature B-Rep.");
        TopTools_IndexedMapOfShape solids; TopExp::MapShapes(shape,TopAbs_SOLID,solids);
        if (solids.Extent()==0) throw gcnew InvalidOperationException("Feature contains no solids.");
        GProp_GProps props; BRepGProp::VolumeProperties(shape,props);
        if (!std::isfinite(props.Mass()) || props.Mass()<=1e-9) throw gcnew InvalidOperationException("Empty feature volume.");
        return props.Mass();
    }
    static String^ encodeFeature(const TopoDS_Shape& shape)
    {
        featureVolume(shape);
        std::ostringstream stream; BRepTools::Write(shape,stream);
        return gcnew String(stream.str().c_str());
    }
    static TopoDS_Shape decodeFeature(String^ body)
    {
        if (String::IsNullOrWhiteSpace(body) || body->Length>64*1024*1024) throw gcnew ArgumentException("Invalid B-Rep data.");
        std::istringstream stream(msclr::interop::marshal_as<std::string>(body));
        TopoDS_Shape shape; BRep_Builder builder; BRepTools::Read(shape,stream,builder);
        featureVolume(shape); return shape;
    }
    String^ OcctFeatureKernel::Extrude(array<double>^ outer, array<double>^ holes, array<double>^ circleHoles,
                                      array<double>^ circle, array<double>^ frame)
    {
        try { return encodeFeature(featurePrism(outer,holes,circleHoles,circle,frame)); }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }
    String^ OcctFeatureKernel::Cut(String^ baseBody, array<double>^ outer, array<double>^ holes,
        array<double>^ circleHoles, array<double>^ circle, array<double>^ frame, bool throughAll)
    {
        try
        {
            validateFeatureFrame(frame);
            TopoDS_Shape baseShape=decodeFeature(baseBody); const double before=featureVolume(baseShape);
            auto f=safe_cast<array<double>^>(frame->Clone());
            if (throughAll)
            {
                Bnd_Box bounds; BRepBndLib::Add(baseShape,bounds);
                double xmin,ymin,zmin,xmax,ymax,zmax; bounds.Get(xmin,ymin,zmin,xmax,ymax,zmax);
                const double sign=f[12]<0?-1.0:1.0;
                double extent=0;
                for(int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                    extent=std::max(extent,sign*(( (x?xmax:xmin)-f[0])*f[9]+((y?ymax:ymin)-f[1])*f[10]+((z?zmax:zmin)-f[2])*f[11]));
                if (extent<=1e-9) throw gcnew InvalidOperationException("No body in cut direction.");
                f[12]=sign*(extent+std::max(1.0,extent*1e-6));
            }
            TopoDS_Shape tool=featurePrism(outer,holes,circleHoles,circle,f);
            featureVolume(tool);
            BRepAlgoAPI_Cut cut(baseShape,tool); cut.Build();
            if (!cut.IsDone()) throw gcnew InvalidOperationException("OCCT Boolean Cut failed.");
            TopoDS_Shape result=cut.Shape(); const double after=featureVolume(result);
            if (before-after<=std::max(1e-8,before*1e-10)) throw gcnew InvalidOperationException("Cut removes no material.");
            return encodeFeature(result);
        }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }
    String^ OcctFeatureKernel::Revolve(String^ baseBody,array<double>^ outer,array<double>^ holes,array<double>^ circleHoles,
        array<double>^ circle,array<double>^ frame,array<double>^ axis,double angleDegrees,bool reverse)
    {
        try
        {
            TopoDS_Shape revolved=featureRevolve(outer,holes,circleHoles,circle,frame,axis,angleDegrees,reverse);
            if(String::IsNullOrWhiteSpace(baseBody))return encodeFeature(revolved);
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            BRepAlgoAPI_Fuse fuse(baseShape,revolved);fuse.Build();
            if(!fuse.IsDone())throw gcnew InvalidOperationException("OCCT revolve fuse failed.");
            return encodeFeature(fuse.Shape());
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    String^ OcctFeatureKernel::RevolveCut(String^ baseBody,array<double>^ outer,array<double>^ holes,
        array<double>^ circleHoles,array<double>^ circle,array<double>^ frame,array<double>^ axis,double angleDegrees,bool reverse)
    {
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);const double before=featureVolume(baseShape);
            TopoDS_Shape tool=featureRevolve(outer,holes,circleHoles,circle,frame,axis,angleDegrees,reverse);
            featureVolume(tool);
            BRepAlgoAPI_Cut cut(baseShape,tool);cut.Build();
            if(!cut.IsDone())throw gcnew InvalidOperationException("OCCT revolved cut failed.");
            TopoDS_Shape result=cut.Shape();const double after=featureVolume(result);
            if(before-after<=std::max(1e-8,before*1e-10))throw gcnew InvalidOperationException("Revolved cut removes no material.");
            return encodeFeature(result);
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    String^ OcctFeatureKernel::Shell(String^ baseBody,array<String^>^ faceReferences,double thickness,bool outward)
    {
        if(!std::isfinite(thickness)||thickness<=1e-9)throw gcnew ArgumentException("Shell thickness must be positive.");
        if(faceReferences==nullptr||faceReferences->Length==0)throw gcnew ArgumentException("Shell requires at least one opening face.");
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            TopTools_ListOfShape closingFaces;
            for each(String^ reference in faceReferences)
                closingFaces.Append(resolveFaceReference(baseShape,reference));

            BRepOffsetAPI_MakeThickSolid shell;
            const double offset=outward?thickness:-thickness;
            shell.MakeThickSolidByJoin(baseShape,closingFaces,offset,1.0e-3);
            if(!shell.IsDone())throw gcnew InvalidOperationException("OCCT shell operation failed.");
            TopoDS_Shape result=shell.Shape();
            return encodeFeature(result);
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    String^ OcctFeatureKernel::Mirror(String^ baseBody,array<double>^ planeFrame,bool keepOriginal)
    {
        if(planeFrame==nullptr||planeFrame->Length!=9)throw gcnew ArgumentException("Mirror plane requires origin, U and normal.");
        for each(double value in planeFrame)if(!std::isfinite(value))throw gcnew ArgumentException("Invalid mirror plane.");
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            gp_Pnt origin(planeFrame[0],planeFrame[1],planeFrame[2]);
            gp_Dir normal(planeFrame[6],planeFrame[7],planeFrame[8]);
            gp_Dir u(planeFrame[3],planeFrame[4],planeFrame[5]);
            gp_Trsf transform;transform.SetMirror(gp_Ax2(origin,normal,u));
            BRepBuilderAPI_Transform mirrorMaker(baseShape,transform,true);
            TopoDS_Shape mirrored=mirrorMaker.Shape();
            featureVolume(mirrored);
            if(!keepOriginal)return encodeFeature(mirrored);

            TopoDS_Compound compound;
            BRep_Builder builder;builder.MakeCompound(compound);
            builder.Add(compound,baseShape);builder.Add(compound,mirrored);
            return encodeFeature(compound);
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    array<String^>^ OcctFeatureKernel::EdgeReferences(String^ body)
    {
        try
        {
            TopoDS_Shape shape=decodeFeature(body);
            TopTools_IndexedMapOfShape edges;
            TopExp::MapShapes(shape,TopAbs_EDGE,edges);
            auto values=gcnew array<String^>(edges.Extent());
            for(int i=1;i<=edges.Extent();++i)
            {
                auto value=encodeEdgeReference(TopoDS::Edge(edges(i)));
                values[i-1]=gcnew String(value.c_str());
            }
            return values;
        }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }

    array<String^>^ OcctFeatureKernel::FaceReferences(String^ body)
    {
        try
        {
            TopoDS_Shape shape=decodeFeature(body);
            TopTools_IndexedMapOfShape faces;
            TopExp::MapShapes(shape,TopAbs_FACE,faces);
            auto values=gcnew System::Collections::Generic::List<String^>();
            for(int i=1;i<=faces.Extent();++i)
            {
                auto value=encodeFaceReference(TopoDS::Face(faces(i)));
                if(!value.empty())values->Add(gcnew String(value.c_str()));
            }
            return values->ToArray();
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    String^ OcctFeatureKernel::Fillet(String^ baseBody,array<String^>^ edgeReferences,double radius)
    {
        if(!std::isfinite(radius)||radius<=1e-9)throw gcnew ArgumentException("Fillet radius must be positive.");
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            auto edges=resolveEdgeReferences(baseShape,edgeReferences);
            BRepFilletAPI_MakeFillet fillet(baseShape);
            for(const auto& edge:edges)fillet.Add(radius,edge);
            fillet.Build();
            if(!fillet.IsDone())throw gcnew InvalidOperationException("OCCT fillet failed.");
            return encodeFeature(fillet.Shape());
        }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }

    String^ OcctFeatureKernel::Chamfer(String^ baseBody,array<String^>^ edgeReferences,double distance)
    {
        if(!std::isfinite(distance)||distance<=1e-9)throw gcnew ArgumentException("Chamfer distance must be positive.");
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            auto edges=resolveEdgeReferences(baseShape,edgeReferences);
            BRepFilletAPI_MakeChamfer chamfer(baseShape);
            for(const auto& edge:edges)chamfer.Add(distance,edge);
            chamfer.Build();
            if(!chamfer.IsDone())throw gcnew InvalidOperationException("OCCT chamfer failed.");
            return encodeFeature(chamfer.Shape());
        }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }

    String^ OcctFeatureKernel::Hole(String^ baseBody,String^ faceReference,
        double centerX,double centerY,double centerZ,double diameter,double depth,bool throughAll,bool reverse)
    {
        if(!std::isfinite(centerX)||!std::isfinite(centerY)||!std::isfinite(centerZ)||
           !std::isfinite(diameter)||diameter<=1e-9||!std::isfinite(depth)||depth<=1e-9)
            throw gcnew ArgumentException("Invalid hole parameters.");
        try
        {
            TopoDS_Shape baseShape=decodeFeature(baseBody);
            const double before=featureVolume(baseShape);
            TopoDS_Face support=resolveFaceReference(baseShape,faceReference);
            gp_Pnt origin;gp_Dir u,v,normal;
            if(!planarFaceFrame(support,origin,u,v,normal))
                throw gcnew InvalidOperationException("Hole support is not planar.");

            gp_Pnt requested(centerX,centerY,centerZ);
            gp_Vec normalVec(normal.X(),normal.Y(),normal.Z());
            const double offset=gp_Vec(origin,requested).Dot(normalVec);
            gp_Pnt center=requested.Translated(normalVec.Multiplied(-offset));

            gp_Dir direction=normal;
            if(reverse)direction.Reverse();
            double length=depth;
            if(throughAll)
            {
                Bnd_Box bounds;BRepBndLib::Add(baseShape,bounds);
                double xmin,ymin,zmin,xmax,ymax,zmax;bounds.Get(xmin,ymin,zmin,xmax,ymax,zmax);
                gp_Vec d(direction.X(),direction.Y(),direction.Z());
                double extent=0.0;
                for(int xi=0;xi<2;xi++)for(int yi=0;yi<2;yi++)for(int zi=0;zi<2;zi++)
                {
                    gp_Pnt corner(xi?xmax:xmin,yi?ymax:ymin,zi?zmax:zmin);
                    extent=std::max(extent,gp_Vec(center,corner).Dot(d));
                }
                if(extent<=1e-9)throw gcnew InvalidOperationException("No body in hole direction.");
                length=extent+std::max(1.0,extent*1e-6);
            }

            const double epsilon=std::max(1e-6,length*1e-7);
            gp_Vec d(direction.X(),direction.Y(),direction.Z());
            gp_Pnt start=center.Translated(d.Multiplied(-epsilon));
            BRepPrimAPI_MakeCylinder cylinder(gp_Ax2(start,direction),diameter*0.5,length+epsilon);
            TopoDS_Shape tool=cylinder.Shape();
            featureVolume(tool);
            BRepAlgoAPI_Cut cut(baseShape,tool);cut.Build();
            if(!cut.IsDone())throw gcnew InvalidOperationException("OCCT hole cut failed.");
            TopoDS_Shape result=cut.Shape();const double after=featureVolume(result);
            if(before-after<=std::max(1e-8,before*1e-10))
                throw gcnew InvalidOperationException("Hole removes no material.");
            return encodeFeature(result);
        }
        catch(const Standard_Failure& ex){throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString()));}
    }

    array<double>^ OcctFeatureKernel::Inspect(String^ body)
    {
        try
        {
            TopoDS_Shape shape=decodeFeature(body); TopTools_IndexedMapOfShape solids,faces,edges;
            TopExp::MapShapes(shape,TopAbs_SOLID,solids);TopExp::MapShapes(shape,TopAbs_FACE,faces);TopExp::MapShapes(shape,TopAbs_EDGE,edges);
            return gcnew array<double>{featureVolume(shape),(double)solids.Extent(),(double)faces.Extent(),(double)edges.Extent()};
        }
        catch (const Standard_Failure& ex) { throw gcnew InvalidOperationException(gcnew String(ex.GetMessageString())); }
    }

    double OcctViewportBridge::MeasureExtrudedProfileVolume(array<double>^ outerUvPairs,
                                                   array<double>^ innerLoopData,
                                                   array<double>^ circleHoles,
                                                   double ox, double oy, double oz,
                                                   double ux, double uy, double uz,
                                                   double vx, double vy, double vz,
                                                   double nx, double ny, double nz,
                                                   double depth)
    {
        // Uses the exact production prism builder; no viewer, mesh or approximation.
        try
        {
            TopoDS_Shape shape = makePrismFromProfile(outerUvPairs, innerLoopData, circleHoles,
                ox, oy, oz, ux, uy, uz, vx, vy, vz, nx, ny, nz, depth);
            if (shape.IsNull()) return -1.0;
            GProp_GProps properties;
            BRepGProp::VolumeProperties(shape, properties);
            return properties.Mass();
        }
        catch (const Standard_Failure&) { return -1.0; }
    }

    bool OcctViewportBridge::PreviewExtrudeProfile(array<double>^ outerUvPairs,
                                                   array<double>^ innerLoopData,
                                                   array<double>^ circleHoles,
                                                   double ox, double oy, double oz,
                                                   double ux, double uy, double uz,
                                                   double vx, double vy, double vz,
                                                   double nx, double ny, double nz,
                                                   double depth)
    {
        if (!IsInitialized() || std::abs(depth) < 1e-9) return false;
        try
        {
            CancelExtrusionPreview();
            TopoDS_Shape shape = makePrismFromProfile(outerUvPairs, innerLoopData, circleHoles,
                                                       ox, oy, oz,
                                                       ux, uy, uz,
                                                       vx, vy, vz,
                                                       nx, ny, nz,
                                                       depth);
            if (shape.IsNull() || shape.ShapeType() != TopAbs_SOLID || !BRepCheck_Analyzer(shape).IsValid()) return false;
            myState->PreviewSolidShape = shape;
            myState->PreviewSolid = displayShape(myState, shape, rgb(0.30, 0.66, 0.38), AIS_Shaded, false, 0.22);
            myState->Context->UpdateCurrentViewer();
            return true;
        }
        catch (const Standard_Failure&)
        {
            return false;
        }
    }

    bool OcctViewportBridge::PreviewExtrudePolyline(array<double>^ uvPairs,
                                                    double ox, double oy, double oz,
                                                    double ux, double uy, double uz,
                                                    double vx, double vy, double vz,
                                                    double nx, double ny, double nz,
                                                    double depth)
    {
        if (!IsInitialized() || std::abs(depth) < 1e-9) return false;
        try
        {
            CancelExtrusionPreview();
            TopoDS_Shape shape = makePrismFromPolyline(uvPairs, ox, oy, oz, ux, uy, uz,
                                                        vx, vy, vz, nx, ny, nz, depth);
            if (shape.IsNull() || shape.ShapeType() != TopAbs_SOLID || !BRepCheck_Analyzer(shape).IsValid()) return false;
            myState->PreviewSolidShape = shape;
            myState->PreviewSolid = displayShape(myState, shape, rgb(0.30, 0.66, 0.38), AIS_Shaded, false, 0.22);
            myState->Context->UpdateCurrentViewer();
            return true;
        }
        catch (const Standard_Failure&)
        {
            return false;
        }
    }

    bool OcctViewportBridge::PreviewExtrudeCircle(double centerU, double centerV, double radius,
                                                  double ox, double oy, double oz,
                                                  double ux, double uy, double uz,
                                                  double vx, double vy, double vz,
                                                  double nx, double ny, double nz,
                                                  double depth)
    {
        if (!IsInitialized()) return false;
        CancelExtrusionPreview();
        if (!std::isfinite(radius) || !std::isfinite(depth) || radius <= 0.0 || std::abs(depth) < 1e-9) return false;
        try
        {
            CancelExtrusionPreview();
            gp_Pnt center = planePoint(centerU, centerV, ox, oy, oz, ux, uy, uz, vx, vy, vz);
            gp_Ax2 ax(center, gp_Dir(nx, ny, nz), gp_Dir(ux, uy, uz));
            BRepBuilderAPI_MakeEdge edgeMaker(gp_Circ(ax, radius));
            if (!edgeMaker.IsDone()) return false;
            TopoDS_Edge edge = edgeMaker.Edge();
            BRepBuilderAPI_MakeWire wireMaker(edge);
            if (!wireMaker.IsDone()) return false;
            TopoDS_Wire wire = wireMaker.Wire();
            BRepBuilderAPI_MakeFace faceMaker(wire, true);
            if (!faceMaker.IsDone()) return false;
            BRepPrimAPI_MakePrism prism(faceMaker.Face(), gp_Vec(nx * depth, ny * depth, nz * depth), true, true);
            TopoDS_Shape shape = prism.Shape();
            if (shape.IsNull() || shape.ShapeType() != TopAbs_SOLID || !BRepCheck_Analyzer(shape).IsValid()) return false;
            myState->PreviewSolidShape = shape;
            myState->PreviewSolid = displayShape(myState, shape, rgb(0.30, 0.66, 0.38), AIS_Shaded, false, 0.22);
            myState->Context->UpdateCurrentViewer();
            return true;
        }
        catch (const Standard_Failure&)
        {
            return false;
        }
    }

    bool OcctViewportBridge::PreviewFeatureBody(String^ body)
    {
        if (!IsInitialized()) return false;
        try
        {
            TopoDS_Shape shape=decodeFeature(body);
            CancelExtrusionPreview();
            myState->PreviewSolid=displayShape(myState,shape,rgb(0.30,0.66,0.38),AIS_Shaded,false,0.22);
            myState->PreviewSolidShape=shape;
            HideCommittedSolid();myState->Context->UpdateCurrentViewer();return true;
        }
        catch (const Standard_Failure&) { CancelExtrusionPreview();ShowCommittedSolid();return false; }
        catch (System::Exception^) { CancelExtrusionPreview();ShowCommittedSolid();return false; }
    }
    bool OcctViewportBridge::PublishFeatureBody(String^ body)
    {
        if (!IsInitialized()) return false;
        Handle(AIS_Shape) candidate;
        Handle(AIS_Shape) previous=myState->SolidPresentation;
        TopoDS_Shape previousShape=myState->SolidShape;
        try
        {
            if (String::IsNullOrEmpty(body)) { ClearModel(); return true; }
            TopoDS_Shape shape=decodeFeature(body);
            // Create the replacement presentation first; retain the previous body
            // until OCCT accepts the new presentation.
            candidate=displayShape(myState,shape,rgb(0.60,0.66,0.72),AIS_Shaded,true);
            if (!myState->SolidPresentation.IsNull())myState->Context->Remove(myState->SolidPresentation,false);
            myState->SolidPresentation=candidate;myState->SolidShape=shape;
            CancelExtrusionPreview();SetSelectionMode(myState->SelectionMode);
            myState->Context->UpdateCurrentViewer();return true;
        }
        catch (const Standard_Failure&)
        {
            if(!candidate.IsNull())myState->Context->Remove(candidate,false);
            myState->SolidPresentation=previous;myState->SolidShape=previousShape;
            if(!previous.IsNull())myState->Context->Display(previous,false);
            ShowCommittedSolid();return false;
        }
        catch (System::Exception^)
        {
            if(!candidate.IsNull())myState->Context->Remove(candidate,false);
            myState->SolidPresentation=previous;myState->SolidShape=previousShape;
            if(!previous.IsNull())myState->Context->Display(previous,false);
            ShowCommittedSolid();return false;
        }
    }

    bool OcctViewportBridge::CommitExtrusion()
    {
        if (!IsInitialized() || myState->PreviewSolidShape.IsNull()) return false;
        try
        {
            if (!BRepCheck_Analyzer(myState->PreviewSolidShape).IsValid()) return false;
            if (!myState->PreviewSolid.IsNull())
            {
                myState->Context->Remove(myState->PreviewSolid, false);
                myState->PreviewSolid.Nullify();
            }
            if (!myState->SolidPresentation.IsNull())
            {
                myState->Context->Remove(myState->SolidPresentation, false);
                myState->SolidPresentation.Nullify();
            }
            myState->SolidShape = myState->PreviewSolidShape;
            myState->PreviewSolidShape.Nullify();
            myState->SolidPresentation = displayShape(myState, myState->SolidShape,
                                                       rgb(0.60, 0.66, 0.72), AIS_Shaded, true);
            myState->Context->Deactivate();
            SetSelectionMode(myState->SelectionMode);
            myState->Context->UpdateCurrentViewer();
            FitAll();
            return true;
        }
        catch (const Standard_Failure&)
        {
            return false;
        }
    }

    void OcctViewportBridge::HideCommittedSolid()
    {
        if (!IsInitialized() || myState->SolidPresentation.IsNull()) return;
        myState->Context->Erase(myState->SolidPresentation, false);
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::ShowCommittedSolid()
    {
        if (!IsInitialized() || myState->SolidPresentation.IsNull() || myState->SolidShape.IsNull()) return;
        myState->Context->Display(myState->SolidPresentation, false);
        myState->Context->Deactivate(myState->SolidPresentation);
        int selectionMode = AIS_Shape::SelectionMode(TopAbs_FACE);
        switch (myState->SelectionMode)
        {
            case CadSelectionMode::Vertex: selectionMode = AIS_Shape::SelectionMode(TopAbs_VERTEX); break;
            case CadSelectionMode::Edge: selectionMode = AIS_Shape::SelectionMode(TopAbs_EDGE); break;
            case CadSelectionMode::Face: selectionMode = AIS_Shape::SelectionMode(TopAbs_FACE); break;
            default: selectionMode = 0; break;
        }
        myState->Context->Activate(myState->SolidPresentation, selectionMode, true);
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::CancelExtrusionPreview()
    {
        if (myState == nullptr || myState->Context.IsNull()) return;
        if (!myState->PreviewSolid.IsNull())
        {
            myState->Context->Remove(myState->PreviewSolid, false);
            myState->PreviewSolid.Nullify();
        }
        myState->PreviewSolidShape.Nullify();
        myState->Context->UpdateCurrentViewer();
    }

    void OcctViewportBridge::ClearModel()
    {
        if (!IsInitialized()) return;
        CancelExtrusionPreview();
        if (!myState->SolidPresentation.IsNull())
        {
            myState->Context->Remove(myState->SolidPresentation, false);
            myState->SolidPresentation.Nullify();
        }
        myState->SolidShape.Nullify();
        ClearSketch();
        ClearPreview();
        myState->Context->ClearSelected(false);
        myState->Context->UpdateCurrentViewer();
    }
}

