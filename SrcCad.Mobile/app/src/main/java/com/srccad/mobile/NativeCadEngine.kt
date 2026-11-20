package com.srccad.mobile

object NativeCadEngine {
    init {
        System.loadLibrary("srccad_mobile")
    }

    external fun nativeEngineStatus(): String
}
