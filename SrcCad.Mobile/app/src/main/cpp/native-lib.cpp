#include <jni.h>

extern "C"
JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeEngineStatus(JNIEnv* env, jobject) {
    return env->NewStringUTF("OCCT engine not linked yet");
}
