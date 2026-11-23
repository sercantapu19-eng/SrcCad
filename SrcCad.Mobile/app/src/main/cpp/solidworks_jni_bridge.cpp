// SPDX-License-Identifier: MIT
// Explicit-selection JNI bridge for OpenSWX. Never guesses a configuration,
// payload or BODY: uncertain or unsupported saved geometry is rejected.
#include <jni.h>
#include <filesystem>
#include <sstream>
#include <limits>
#include <string>
#include "openswx/container.h"
#include "openswx/document.h"
#include "openswx/saved_geometry.h"
#include "openswstep/export.h"

namespace {
std::string esc(const std::string& s) {
    std::ostringstream o;
    for (unsigned char c : s) {
        switch(c) {
        case '"': o << "\\\""; break;
        case '\\': o << "\\\\"; break;
        case '\n': o << "\\n"; break;
        case '\r': o << "\\r"; break;
        case '\t': o << "\\t"; break;
        default: if(c >= 0x20) o << static_cast<char>(c);
        }
    }
    return o.str();
}
jstring js(JNIEnv* e, const std::string& s) { return e->NewStringUTF(s.c_str()); }
std::string str(JNIEnv* e, jstring v) {
    if(!v) return {};
    const char* p=e->GetStringUTFChars(v,nullptr);
    if(!p) return {};
    std::string s(p); e->ReleaseStringUTFChars(v,p); return s;
}
}

extern "C" JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeInspectSolidWorks(JNIEnv* e,jobject,jstring jp) {
    try {
        auto path=std::filesystem::path(str(e,jp));
        auto doc=openswx::SwxDocument::Open(path);
        if(!doc.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(doc.error().message)+"\"}");
        auto container=openswx::InspectContainer(path);
        if(!container.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(container.error().message)+"\"}");
        auto extracted=openswx::ExtractParasolidStreams(container.value());
        std::ostringstream o;
        const auto& d=doc.value().doc();
        o << "{\"ok\":true,\"docType\":" << (d.type==openswx::DocumentType::kPart?1:d.type==openswx::DocumentType::kAssembly?2:3)
          << ",\"version\":" << d.version << ",\"previewAvailable\":" << (d.preview_png.empty()?"false":"true")
          << ",\"payloadCount\":" << extracted.payloads.size() << ",\"configurations\":[";
        bool fc=true;
        for(const auto& cfg:d.configurations) {
            if(!fc)o<<','; fc=false;
            o << "{\"name\":\""<<esc(cfg.name)<<"\",\"index\":"<<cfg.index<<",\"candidates\":[";
            auto rr=openswx::ResolveSavedGeometry(container.value(),cfg.name);
            bool fca=true;
            if(rr.ok()) for(const auto& c:rr.value().candidates) {
                if(!fca)o<<','; fca=false;
                o << "{\"payloadIndex\":"<<c.payload_index<<",\"role\":\""<<openswx::ToString(c.role)
                  <<"\",\"kind\":\""<<openswx::ToString(c.kind)<<"\",\"selectedPartition\":"<<(c.selected_partition?"true":"false")
                  <<",\"decodedComplete\":"<<(c.decoded_complete?"true":"false")<<",\"bodies\":[";
                bool fb=true;
                for(const auto& b:c.bodies) { if(!fb)o<<','; fb=false; o<<"{\"id\":"<<b.body_id<<",\"kind\":\""<<openswx::ToString(b.kind)<<"\"}"; }
                o << "]}";
            }
            o << "]}";
        }
        o << "]}"; return js(e,o.str());
    } catch(const std::exception& x) { return js(e,"{\"ok\":false,\"message\":\""+esc(x.what())+"\"}"); }
      catch(...) { return js(e,"{\"ok\":false,\"message\":\"Unknown SolidWorks inspection error\"}"); }
}

extern "C" JNIEXPORT jbyteArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeSolidWorksPreview(JNIEnv* e,jobject,jstring jp) {
    try {
        auto d=openswx::SwxDocument::Open(std::filesystem::path(str(e,jp)));
        if(!d.ok() || d.value().doc().preview_png.empty()) return e->NewByteArray(0);
        const auto& p=d.value().doc().preview_png;
        if(p.size()>static_cast<size_t>(std::numeric_limits<jsize>::max())) return e->NewByteArray(0);
        auto a=e->NewByteArray(static_cast<jsize>(p.size()));
        if(a)e->SetByteArrayRegion(a,0,static_cast<jsize>(p.size()),reinterpret_cast<const jbyte*>(p.data()));
        return a;
    } catch(...) { return e->NewByteArray(0); }
}

extern "C" JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeSolidWorksToStepSelected(
    JNIEnv* e,jobject,jstring jp,jstring jc,jint pi,jint bi,jstring jo) {
    try {
        if(pi<0 || bi<=0) return js(e,"{\"ok\":false,\"message\":\"Payload ve BODY kimliği seçilmelidir.\"}");
        auto input=std::filesystem::path(str(e,jp)); auto output=std::filesystem::path(str(e,jo));
        std::string config=str(e,jc);
        if(config.empty()||output.empty()) return js(e,"{\"ok\":false,\"message\":\"Konfigürasyon ve çıktı yolu zorunludur.\"}");
        auto container=openswx::InspectContainer(input);
        if(!container.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(container.error().message)+"\"}");
        auto resolution=openswx::ResolveSavedGeometry(container.value(),config);
        if(!resolution.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(resolution.error().message)+"\"}");
        const openswx::SavedGeometryCandidate* chosen=nullptr;
        for(const auto& c:resolution.value().candidates) if(c.payload_index==static_cast<size_t>(pi)){chosen=&c;break;}
        if(!chosen||!chosen->selected_partition||chosen->role!=openswx::GeometrySourceRole::kPartition||!chosen->decoded_complete)
            return js(e,"{\"ok\":false,\"message\":\"Payload doğrulanmış partition değil; otomatik seçim yapılmadı.\"}");
        bool found=false,solid=false;
        for(const auto& b:chosen->bodies) if(b.body_id==static_cast<uint32_t>(bi)){found=true;solid=b.kind==openswx::SavedBodyKind::kSolid;break;}
        if(!found||!solid) return js(e,"{\"ok\":false,\"message\":\"Seçilen BODY doğrulanmış katı gövde değil.\"}");
        if(resolution.value().needs_rebuild.value_or(false))
            return js(e,"{\"ok\":false,\"message\":\"Konfigürasyon yeniden oluşturulmalı; eski geometri aktarılmadı.\"}");
        if(resolution.value().state_status==openswx::GeometryResolutionStatus::kUnsupported)
            return js(e,"{\"ok\":false,\"message\":\"Delta/geçmiş verisi desteklenmiyor; son durum güvenle oluşturulamadı.\"}");
        auto payloads=openswx::ExtractParasolidStreams(container.value());
        if(static_cast<size_t>(pi)>=payloads.payloads.size()) return js(e,"{\"ok\":false,\"message\":\"Payload bulunamadı.\"}");
        openswstep::ExportOptions opts; opts.length_unit=openswstep::LengthUnit::kMetre; opts.product_name="SrcCad SolidWorks import";
        auto step=openswstep::ExportPayload(payloads.payloads[static_cast<size_t>(pi)],openparasolid::NodeId{static_cast<uint32_t>(bi)},opts);
        if(!step.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(step.error().message)+"\"}");
        auto written=openswstep::WriteStepFile(step.value(),output);
        if(!written.ok()) return js(e,"{\"ok\":false,\"message\":\""+esc(written.error().message)+"\"}");
        std::ostringstream o; o<<"{\"ok\":true,\"partial\":false,\"configuration\":\""<<esc(config)<<"\",\"payloadIndex\":"<<pi<<",\"bodyId\":"<<bi<<",\"bytes\":"<<written.value()<<"}";
        return js(e,o.str());
    } catch(const std::exception& x) { return js(e,"{\"ok\":false,\"message\":\""+esc(x.what())+"\"}"); }
      catch(...) { return js(e,"{\"ok\":false,\"message\":\"Unknown SolidWorks STEP export error\"}"); }
}
