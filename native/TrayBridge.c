/* Read-only Shell_NotifyIcon message observer for the Explorer taskbar thread.
   Does not replace a shell window, alter the message, or intercept other threads.
   Windows serializes NOTIFYICONDATA fields using 32-bit handles on this wire. */
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <string.h>

typedef struct {
 DWORD magic,operation,window,id,flags,callback,icon,version,state,mask,pid;
 GUID guid;
 WCHAR tip[128];
} RUSHBIN_TRAY_EVENT;

__declspec(dllexport) LRESULT CALLBACK RushbinTrayHook(int code,WPARAM wp,LPARAM lp){
 if(code>=0 && lp){
  const CWPSTRUCT *msg=(const CWPSTRUCT*)lp;
  if(msg->message==WM_COPYDATA && msg->lParam){
   const COPYDATASTRUCT *cd=(const COPYDATASTRUCT*)msg->lParam;
   if(cd->dwData==1 && cd->lpData && cd->cbData>=32 && cd->cbData<=4096){
    WCHAR cls[40];
    if(GetClassNameW(msg->hwnd,cls,40) && !lstrcmpW(cls,L"Shell_TrayWnd")){
     const BYTE *data=(const BYTE*)cd->lpData;
     DWORD op=*(const DWORD*)(data+4),size=*(const DWORD*)(data+8);
     if(*(const DWORD*)data==0x34753423 && (op<=2 || op==4) && size>=24 && size<=4096){
      if(size>cd->cbData-8)size=cd->cbData-8;
      HWND receiver=FindWindowW(NULL,L"rushbin tray observer v1");
      if(receiver){
       const BYTE *nid=data+8;RUSHBIN_TRAY_EVENT event;ZeroMemory(&event,sizeof(event));
       event.magic=0x52544231;event.operation=op;event.window=*(const DWORD*)(nid+4);
       event.id=*(const DWORD*)(nid+8);event.flags=*(const DWORD*)(nid+12);
       event.callback=*(const DWORD*)(nid+16);event.icon=*(const DWORD*)(nid+20);
       GetWindowThreadProcessId((HWND)(ULONG_PTR)event.window,&event.pid);
       if(size>=280)memcpy(event.tip,nid+24,256);
       if(size>=288){event.state=*(const DWORD*)(nid+280);event.mask=*(const DWORD*)(nid+284);}
       if(size>=804){DWORD version=*(const DWORD*)(nid+800);if(op==4 || ((op==0 || op==1) && !(event.flags&16) && (version==3 || version==4)))event.version=version;}
       if(size>=952 && (event.flags&32))memcpy(&event.guid,nid+936,16);
       event.tip[127]=0;
       COPYDATASTRUCT out;out.dwData=0x52544231;out.cbData=sizeof(event);out.lpData=&event;
       DWORD_PTR result=0;
       SendMessageTimeoutW(receiver,WM_COPYDATA,(WPARAM)msg->hwnd,(LPARAM)&out,SMTO_ABORTIFHUNG|SMTO_BLOCK,40,&result);
      }
     }
    }
   }
  }
 }
 return CallNextHookEx(NULL,code,wp,lp);
}

BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved){return TRUE;}
