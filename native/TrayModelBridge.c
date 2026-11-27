/* Explorer owns the active list and every action. This adapter supports only
   the verified Taskbar.dll symbol layout and runs on its taskbar thread. */
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <string.h>

/* TinyCC ships the Ole32 import library without the COM SDK headers. */
__declspec(dllimport) HRESULT WINAPI CoCreateInstance(const GUID *, void *, DWORD, const GUID *, void **);

enum {
    TRAY_REQUEST = 0x52544D32, TRAY_LIMIT = 96, TRAY_BITMAP_SIDE = 128,
    TRAY_READ = 1, TRAY_MOVE = 2, TRAY_CLICK = 3, TRAY_DOUBLE_CLICK = 4, TRAY_CONTEXT = 5,
    TRAY_MIDDLE_CLICK = 6, TRAY_TIP_OPEN = 7, TRAY_TIP_CLOSE = 8
};
enum {
    TRAY_UI_GLOBAL = 0x250948, TRAY_UI_GET_MODEL = 0x82850,
    TRAY_MODEL_THREAD = 0x24efd0,
    MODEL_OVERFLOW = 0xaa2e0, MODEL_MOVE = 0x144200,
    ICON_BITMAP = 0x30740, ICON_TOOLTIP = 0x73ab0, ICON_DISPLAY_NAME = 0x15df30,
    ICON_CLICK = 0xd02b0, ICON_CONTEXT = 0x15bff0, ICON_DOUBLE_CLICK = 0x15c3a0,
    ICON_OWNS_TIP = 0x84c80, ICON_REQUEST_TIP = 0xa8f60
};

typedef struct {
    DWORD serial, action;
    ULONGLONG source, target;
    LONG after, x, y;
    WCHAR map[56];
} TrayRequest;

typedef struct {
    ULONGLONG token;
    UINT width, height, flags;
    WCHAR tip[256];
    BYTE pixels[TRAY_BITMAP_SIDE * TRAY_BITMAP_SIDE * 4];
    UINT imageHash;
} TrayItem;

typedef struct {
    DWORD serial;
    LONG result;
    DWORD count, explorer;
    TrayItem items[TRAY_LIMIT];
} TrayReply;

typedef HRESULT (WINAPI *GetComValue)(void *, void **);
typedef HRESULT (WINAPI *GetAt)(void *, UINT, void **);
typedef HRESULT (WINAPI *GetCount)(void *, UINT *);
typedef HRESULT (WINAPI *GetBool)(void *, BYTE *);
typedef HRESULT (WINAPI *QueryInterface)(void *, const GUID *, void **);
typedef ULONG (WINAPI *Release)(void *);
typedef HRESULT (WINAPI *MoveIcon)(void *, void *, int, UINT);
typedef HRESULT (WINAPI *ReadBitmapSize)(void *, UINT *, UINT *);
typedef HRESULT (WINAPI *ReadBitmapFormat)(void *, GUID *);
typedef HRESULT (WINAPI *CopyBitmap)(void *, const RECT *, UINT, UINT, BYTE *);
typedef HRESULT (WINAPI *InitializeBitmapScaler)(void *, void *, UINT, UINT, UINT);
typedef struct { float x, y; } NativePoint;
typedef HRESULT (WINAPI *IconAction)(void *, BYTE, NativePoint);
typedef HRESULT (WINAPI *IconMiddleAction)(void *, NativePoint);
typedef const WCHAR *(WINAPI *GetStringBuffer)(void *, UINT *);
typedef HRESULT (WINAPI *DeleteString)(void *);

static const GUID taskbarPdb = {0x63c1425a,0x7856,0xe31e,{0x2d,0x8c,0x1b,0xce,0xe4,0x38,0xef,0x9e}};
static const GUID model5 = {0x22fe2d45,0x26c1,0x569c,{0xb1,0x7e,0x42,0x62,0x6a,0xc3,0x93,0x75}};
static const GUID model6 = {0x67d74d37,0xabfc,0x54c9,{0x91,0xbd,0xcd,0x40,0xba,0x11,0xdb,0x58}};
static const GUID icon2 = {0xa28815ef,0x846a,0x5081,{0xbe,0xa9,0xd1,0xb0,0x89,0x6c,0x8d,0x89}};
static const GUID icon6 = {0xd76cad80,0x3103,0x54f4,{0x9a,0xbe,0x5f,0x04,0x7c,0xbf,0xe8,0x5c}};
static const GUID icon7 = {0x31e744d0,0xf386,0x5099,{0x8c,0x29,0xbc,0x37,0x1c,0x71,0xf8,0x95}};
static const GUID softwareBitmapNative = {0x94bc8415,0x04ea,0x4b2e,{0xaf,0x13,0x4d,0xe9,0x5a,0xa8,0x98,0xeb}};
static const GUID wicBitmap = {0x00000121,0xa8f2,0x4877,{0xba,0x0a,0xfd,0x2b,0x66,0x45,0xfb,0x94}};
static const GUID wicFactoryClass = {0xcacaf262,0x9370,0x4615,{0xa1,0x3b,0x9f,0x55,0x39,0xda,0x4c,0x0a}};
static const GUID wicFactoryInterface = {0xec5ec8a9,0xc395,0x4314,{0x9c,0x77,0x54,0xd7,0xa9,0x35,0xff,0x70}};
static const GUID wicBgra = {0x6fddc324,0x4e03,0x4bfe,{0xb1,0x85,0x3d,0x77,0x76,0x8d,0xc9,0x0f}};
static const GUID wicPbgra = {0x6fddc324,0x4e03,0x4bfe,{0xb1,0x85,0x3d,0x77,0x76,0x8d,0xc9,0x10}};

static void **Vtable(void *object) { return *(void ***)object; }
static void ReleaseObject(void *object) { if (object) ((Release)Vtable(object)[2])(object); }
static HRESULT Query(void *object, const GUID *iid, void **result) {
    return ((QueryInterface)Vtable(object)[0])(object,iid,result);
}

static BYTE *VerifiedModule(void) {
    BYTE *base = (BYTE *)GetModuleHandleW(L"Taskbar.dll");
    if (!base) return NULL;
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return NULL;
    IMAGE_NT_HEADERS64 *pe = (IMAGE_NT_HEADERS64 *)(base + dos->e_lfanew);
    if (pe->Signature != IMAGE_NT_SIGNATURE || pe->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        pe->OptionalHeader.SizeOfImage <= TRAY_UI_GLOBAL + sizeof(void *)) return NULL;
    IMAGE_DATA_DIRECTORY debug = pe->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    if ((ULONGLONG)debug.VirtualAddress + debug.Size > pe->OptionalHeader.SizeOfImage) return NULL;
    IMAGE_DEBUG_DIRECTORY *entries = (IMAGE_DEBUG_DIRECTORY *)(base + debug.VirtualAddress);
    for (UINT i = 0; i < debug.Size / sizeof(*entries); i++) {
        if (entries[i].Type != IMAGE_DEBUG_TYPE_CODEVIEW || entries[i].SizeOfData < 24 ||
            (ULONGLONG)entries[i].AddressOfRawData + entries[i].SizeOfData > pe->OptionalHeader.SizeOfImage)
            continue;
        BYTE *data = base + entries[i].AddressOfRawData;
        if (!memcmp(data,"RSDS",4) && !memcmp(data+4,&taskbarPdb,16) && *(DWORD *)(data+20) == 1)
            return base;
    }
    return NULL;
}

static ULONGLONG IconToken(void *icon,const TrayRequest *request) {
    ULONGLONG token = (ULONGLONG)(ULONG_PTR)icon;
    for (UINT i=0; i<sizeof(request->map)/sizeof(WCHAR) && request->map[i]; i++)
        token = (token ^ request->map[i]) * 1099511628211ULL;
    token ^= token >> 30; token *= 0xbf58476d1ce4e5b9ULL;
    token ^= token >> 27; token *= 0x94d049bb133111ebULL;
    return token ^ (token >> 31);
}

static void ReadTip(void *icon, HRESULT (WINAPI *getter)(void *,void **), WCHAR *output) {
    HMODULE combase = GetModuleHandleW(L"combase.dll");
    GetStringBuffer data = (GetStringBuffer)GetProcAddress(combase,"WindowsGetStringRawBuffer");
    DeleteString delete = (DeleteString)GetProcAddress(combase,"WindowsDeleteString");
    void *text = NULL;
    if (!data || !delete || FAILED(getter(icon,&text)) || !text) return;
    UINT length = 0;
    const WCHAR *raw = data(text,&length);
    if (raw && length) {
        if (length > 255) length = 255;
        memcpy(output,raw,length*sizeof(WCHAR));
        output[length] = 0;
    }
    delete(text);
}

/* Keep the transfer bounded while retaining the original image and alpha. */
static void *FitBitmap(void *bitmap, UINT *width, UINT *height) {
    UINT longest = *width > *height ? *width : *height;
    UINT scaledWidth = (UINT)(((ULONGLONG)*width * TRAY_BITMAP_SIDE + longest/2) / longest);
    UINT scaledHeight = (UINT)(((ULONGLONG)*height * TRAY_BITMAP_SIDE + longest/2) / longest);
    if (!scaledWidth) scaledWidth = 1;
    if (!scaledHeight) scaledHeight = 1;
    void *factory = NULL, *scaler = NULL;
    if (FAILED(CoCreateInstance(&wicFactoryClass,NULL,1 /* CLSCTX_INPROC_SERVER */,
                                &wicFactoryInterface,&factory)))
        return NULL;
    /* IWICImagingFactory::CreateBitmapScaler, then IWICBitmapScaler::Initialize (Fant). */
    HRESULT result = ((GetComValue)Vtable(factory)[11])(factory,&scaler);
    ReleaseObject(factory);
    if (FAILED(result) || !scaler) return NULL;
    result = ((InitializeBitmapScaler)Vtable(scaler)[8])(scaler,bitmap,scaledWidth,scaledHeight,3);
    if (FAILED(result)) {
        ReleaseObject(scaler);
        return NULL;
    }
    *width = scaledWidth;
    *height = scaledHeight;
    return scaler;
}

static void ReadImage(BYTE *base, void *icon, TrayItem *item) {
    void *bitmap = NULL, *interop = NULL, *wic = NULL, *scaler = NULL;
    if (FAILED(((GetComValue)(base + ICON_BITMAP))(icon,&bitmap)) || !bitmap) goto done;
    if (FAILED(Query(bitmap,&softwareBitmapNative,&interop)) || !interop) goto done;
    if (FAILED(((QueryInterface)Vtable(interop)[6])(interop,&wicBitmap,&wic)) || !wic) goto done;
    UINT width = 0, height = 0;
    if (FAILED(((ReadBitmapSize)Vtable(wic)[3])(wic,&width,&height)) || !width || !height) goto done;
    if (width > TRAY_BITMAP_SIDE || height > TRAY_BITMAP_SIDE) {
        scaler = FitBitmap(wic,&width,&height);
        if (!scaler) goto done;
    }
    void *source = scaler ? scaler : wic;
    GUID format;
    if (FAILED(((ReadBitmapFormat)Vtable(source)[4])(source,&format))) goto done;
    BOOL premultiplied = !memcmp(&format,&wicPbgra,16);
    if (!premultiplied && memcmp(&format,&wicBgra,16)) goto done;
    if (FAILED(((CopyBitmap)Vtable(source)[7])(source,NULL,width*4,width*height*4,item->pixels))) goto done;
    item->width = width;
    item->height = height;
    item->flags |= premultiplied ? 1 : 0;
    UINT hash = 2166136261U;
    for (UINT i=0; i<width*height*4; i++) hash = (hash ^ item->pixels[i]) * 16777619U;
    item->imageHash = hash;
done:
    ReleaseObject(scaler);
    ReleaseObject(wic);
    ReleaseObject(interop);
    ReleaseObject(bitmap);
}

static HRESULT UseOverflow(const TrayRequest *request, TrayReply *reply) {
    BYTE *base = VerifiedModule();
    if (!base || *(DWORD *)(base + TRAY_MODEL_THREAD) != GetCurrentThreadId())
        return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    BYTE *tray = *(BYTE **)(base + TRAY_UI_GLOBAL);
    if (!tray) return E_PENDING;
    void *model = NULL, *overflowModel = NULL, *vector = NULL, *source = NULL;
    HRESULT result = ((GetComValue)(base + TRAY_UI_GET_MODEL))(tray+8,&model);
    if (FAILED(result) || !model) { result = E_PENDING; goto done; }
    result = Query(model,&model6,&overflowModel);
    if (FAILED(result) || !overflowModel || Vtable(overflowModel)[6] != base + MODEL_OVERFLOW) {
        result = E_NOINTERFACE;
        goto done;
    }
    result = ((GetComValue)Vtable(overflowModel)[6])(overflowModel,&vector);
    if (FAILED(result) || !vector) goto done;
    UINT count = 0, sourceIndex = TRAY_LIMIT, targetIndex = TRAY_LIMIT;
    result = ((GetCount)Vtable(vector)[7])(vector,&count);
    if (FAILED(result)) goto done;
    if (count > TRAY_LIMIT) { result = HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW); goto done; }
    reply->count = count;
    for (UINT i = 0; i < count; i++) {
        void *icon = NULL;
        result = ((GetAt)Vtable(vector)[6])(vector,i,&icon);
        if (FAILED(result) || !icon) { result = E_UNEXPECTED; goto done; }
        ULONGLONG token = IconToken(icon,request);
        if (request->action == TRAY_READ) {
            TrayItem *item = &reply->items[i];
            item->token = token;
            item->width = item->height = item->flags = item->imageHash = 0;
            item->tip[0] = 0;
            ReadTip(icon,(GetComValue)(base + ICON_TOOLTIP),item->tip);
            if (!item->tip[0]) {
                void *name = NULL;
                if (SUCCEEDED(Query(icon,&icon6,&name)) && name)
                    ReadTip(name,(GetComValue)(base + ICON_DISPLAY_NAME),item->tip);
                ReleaseObject(name);
            }
            ReadImage(base,icon,item);
            BYTE ownsTip = 0;
            if (SUCCEEDED(((GetBool)(base + ICON_OWNS_TIP))(icon,&ownsTip)) && ownsTip) item->flags |= 2;
        } else {
            if (token == request->source && !source) {
                source = icon;
                icon = NULL;
                sourceIndex = i;
            }
            if (token == request->target) targetIndex = i;
        }
        ReleaseObject(icon);
    }
    if (request->action == TRAY_READ) goto done;
    if (!source) { result = HRESULT_FROM_WIN32(ERROR_NOT_FOUND); goto done; }
    if (request->action == TRAY_MOVE) {
        if (targetIndex == TRAY_LIMIT) { result = HRESULT_FROM_WIN32(ERROR_NOT_FOUND); goto done; }
        if (sourceIndex == targetIndex) { result = S_FALSE; goto done; }
        UINT index = targetIndex + (request->after ? 1 : 0) - (sourceIndex < targetIndex ? 1 : 0);
        if (index >= count) index = count - 1;
        if (sourceIndex == index) { result = S_FALSE; goto done; }
        void *moveModel = NULL;
        result = Query(model,&model5,&moveModel);
        if (SUCCEEDED(result) && moveModel && Vtable(moveModel)[6] == base + MODEL_MOVE)
            result = ((MoveIcon)Vtable(moveModel)[6])(moveModel,source,1,index);
        else result = E_NOINTERFACE;
        ReleaseObject(moveModel);
    } else {
        NativePoint point = { (float)request->x, (float)request->y };
        if (request->action == TRAY_CLICK)
            result = ((IconAction)(base + ICON_CLICK))(source,FALSE,point);
        else if (request->action == TRAY_CONTEXT)
            result = ((IconAction)(base + ICON_CONTEXT))(source,FALSE,point);
        else if (request->action == TRAY_TIP_OPEN || request->action == TRAY_TIP_CLOSE)
            result = ((IconAction)(base + ICON_REQUEST_TIP))(source,request->action == TRAY_TIP_OPEN,point);
        else if (request->action == TRAY_DOUBLE_CLICK) {
            void *doubleClick = NULL;
            result = Query(source,&icon2,&doubleClick);
            if (SUCCEEDED(result) && doubleClick)
                result = ((IconAction)(base + ICON_DOUBLE_CLICK))(doubleClick,FALSE,point);
            ReleaseObject(doubleClick);
        } else if (request->action == TRAY_MIDDLE_CLICK) {
            void *middle = NULL;
            result = Query(source,&icon7,&middle);
            if (SUCCEEDED(result) && middle)
                result = ((IconMiddleAction)Vtable(middle)[6])(middle,point);
            ReleaseObject(middle);
        }
    }
done:
    ReleaseObject(source);
    ReleaseObject(vector);
    ReleaseObject(overflowModel);
    ReleaseObject(model);
    return result;
}

void HandleNativeTrayRequest(const CWPSTRUCT *message) {
    if (message->message != WM_COPYDATA || !message->lParam) return;
    const COPYDATASTRUCT *copy = (const COPYDATASTRUCT *)message->lParam;
    if (copy->dwData != TRAY_REQUEST || copy->cbData != sizeof(TrayRequest) || !copy->lpData) return;
    WCHAR trayClass[32], senderName[48];
    if (!GetClassNameW(message->hwnd,trayClass,32) || lstrcmpW(trayClass,L"Shell_TrayWnd") ||
        !GetWindowTextW((HWND)message->wParam,senderName,48) ||
        lstrcmpW(senderName,L"rushbin native tray client v1")) return;
    TrayRequest request;
    memcpy(&request,copy->lpData,sizeof(request));
    if (request.action < TRAY_READ || request.action > TRAY_TIP_CLOSE || !request.serial) return;
    if (request.action != TRAY_READ) {
        DWORD sender = 0, observer = 0;
        GetWindowThreadProcessId((HWND)message->wParam,&sender);
        GetWindowThreadProcessId(FindWindowW(NULL,L"rushbin tray observer v1"),&observer);
        if (!sender || sender != observer) return;
    }

    static const WCHAR prefix[] = L"Local\\Rushbin.Tray.";
    UINT prefixLength = sizeof(prefix)/sizeof(WCHAR)-1;
    if (memcmp(request.map,prefix,prefixLength*sizeof(WCHAR)) || request.map[prefixLength+32]) return;
    for (UINT i = prefixLength; i < prefixLength+32; i++) {
        WCHAR c = request.map[i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return;
    }
    HANDLE mapping = OpenFileMappingW(FILE_MAP_WRITE,FALSE,request.map);
    if (!mapping) return;
    TrayReply *reply = (TrayReply *)MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,sizeof(TrayReply));
    if (reply) {
        reply->count = 0;
        reply->explorer = GetCurrentProcessId();
        reply->result = UseOverflow(&request,reply);
        InterlockedExchange((volatile LONG *)&reply->serial,request.serial);
        UnmapViewOfFile(reply);
    }
    CloseHandle(mapping);
}
