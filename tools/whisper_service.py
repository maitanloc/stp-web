import os
import sys
import io
import time

# Ensure CUDA DLLs from PyTorch are available for CTranslate2
try:
    import torch
    torch_lib = os.path.join(os.path.dirname(torch.__file__), "lib")
    if os.path.isdir(torch_lib):
        os.add_dll_directory(torch_lib)
except Exception as e:
    print(f"Warning: could not add torch lib to dll directory: {e}")

# Patch av.open if av >= 14 removed metadata_errors kwarg
try:
    import av
    _orig_open = av.open
    def _patched_open(*args, **kwargs):
        kwargs.pop("metadata_errors", None)
        return _orig_open(*args, **kwargs)
    av.open = _patched_open
except Exception:
    pass

import hashlib
from fastapi import FastAPI, UploadFile, File, Form, HTTPException, Response
from fastapi.middleware.cors import CORSMiddleware
from faster_whisper import WhisperModel
from gtts import gTTS

MODEL_NAME_OR_DIR = os.environ.get("WHISPER_MODEL", "large-v3-turbo")
DEVICE = os.environ.get("WHISPER_DEVICE", "cuda")
COMPUTE_TYPE = os.environ.get("WHISPER_COMPUTE_TYPE", "float16")

print(f"[WhisperService] Loading model '{MODEL_NAME_OR_DIR}' on device='{DEVICE}', compute_type='{COMPUTE_TYPE}'...")
t0 = time.time()
try:
    try:
        whisper_model = WhisperModel(MODEL_NAME_OR_DIR, device=DEVICE, compute_type=COMPUTE_TYPE, local_files_only=True)
    except Exception:
        whisper_model = WhisperModel(MODEL_NAME_OR_DIR, device=DEVICE, compute_type=COMPUTE_TYPE)
    print(f"[WhisperService] Model loaded in {time.time() - t0:.2f}s!")
except Exception as ex:
    print(f"[WhisperService] Failed loading '{MODEL_NAME_OR_DIR}' on GPU ({ex}), falling back to local whisper-base...")
    fallback_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "models", "whisper-base"))
    try:
        whisper_model = WhisperModel(fallback_dir, device="cuda", compute_type="float16")
        MODEL_NAME_OR_DIR = "whisper-base"
    except Exception:
        whisper_model = WhisperModel(fallback_dir, device="cpu", compute_type="int8")
        DEVICE = "cpu"
        COMPUTE_TYPE = "int8"
        MODEL_NAME_OR_DIR = "whisper-base"

app = FastAPI(title="STP Whisper Local Service")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

DEFAULT_PROMPT = (
    "Đăng ký kết hôn, giấy khai sinh, căn cước công dân, chứng minh nhân dân, "
    "giấy xác nhận tình trạng hôn nhân, sổ hộ khẩu, tờ khai, số tờ, năm 1990, 1995, 2000, "
    "dòng 1, dòng 2, dòng 3, dòng 4, dòng 5, lọc số, từ số đến số, "
    "bộ công hàm, bản công hàm, phiếu đề xuất, sơ yếu lý lịch, giấy khám sức khỏe, ảnh, "
    "chứng chỉ chuyên khoa, áp vào dòng lọc còn thiếu, áp dòng thiếu, xóa hết giấy tờ, "
    "trang sau, trang trước, hoàn tác, làm lại, xuất excel, đồng bộ vào kho."
)

@app.get("/health")
def health():
    return {
        "status": "healthy",
        "device": DEVICE,
        "compute_type": COMPUTE_TYPE,
        "model": MODEL_NAME_OR_DIR
    }

@app.post("/transcribe")
async def transcribe(
    file: UploadFile = File(...),
    language: str = Form("vi"),
    initial_prompt: str = Form(DEFAULT_PROMPT)
):
    try:
        content = await file.read()
        if not content or len(content) < 100:
            return {"text": "", "language": language, "duration": 0.0}

        audio_stream = io.BytesIO(content)
        t_start = time.time()
        
        # Tinh chỉnh VAD cho giọng nói thì thầm (whisper): threshold thấp (0.25) để không cắt nhầm tiếng thì thào
        segments, info = whisper_model.transcribe(
            audio_stream,
            language=language if language else None,
            initial_prompt=initial_prompt if initial_prompt else None,
            beam_size=5,
            vad_filter=True,
            vad_parameters=dict(threshold=0.25, min_silence_duration_ms=400, speech_pad_ms=250)
        )
        
        texts = [seg.text.strip() for seg in segments if seg.text.strip()]
        full_text = " ".join(texts)
        duration = time.time() - t_start

        return {
            "text": full_text,
            "language": info.language,
            "duration": round(duration, 3)
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

_tts_cache: dict[str, bytes] = {}
TTS_CACHE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "data", "tts_cache"))
os.makedirs(TTS_CACHE_DIR, exist_ok=True)

@app.get("/tts")
def tts_endpoint(text: str):
    clean = text.strip()
    if not clean:
        raise HTTPException(status_code=400, detail="Empty text")

    # 1. Check in-memory cache (siêu tốc ~1ms)
    if clean in _tts_cache:
        return Response(content=_tts_cache[clean], media_type="audio/mpeg")

    # 2. Check disk cache
    h = hashlib.md5(clean.encode("utf-8")).hexdigest()
    disk_file = os.path.join(TTS_CACHE_DIR, f"{h}.mp3")
    if os.path.isfile(disk_file):
        try:
            with open(disk_file, "rb") as f:
                data = f.read()
            _tts_cache[clean] = data
            return Response(content=data, media_type="audio/mpeg")
        except Exception:
            pass

    # 3. Tổng hợp giọng đọc tiếng Việt chuẩn (Google Vietnamese TTS qua google.com.vn)
    try:
        buf = io.BytesIO()
        tts_obj = gTTS(text=clean, lang="vi", tld="com.vn")
        tts_obj.write_to_fp(buf)
        audio_data = buf.getvalue()

        try:
            with open(disk_file, "wb") as f:
                f.write(audio_data)
        except Exception:
            pass

        if len(_tts_cache) < 500:
            _tts_cache[clean] = audio_data

        return Response(content=audio_data, media_type="audio/mpeg")
    except Exception as e:
        print(f"[TTS Error] {e}")
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get("WHISPER_PORT", 5216))
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="info")

