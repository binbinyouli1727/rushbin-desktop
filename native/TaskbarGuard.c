#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdlib.h>
#include <string.h>
typedef struct {DWORD cbSize;HWND hWnd;UINT callback,edge;RECT rectangle;LPARAM lParam;} APPBARDATA;
__declspec(dllimport) UINT_PTR WINAPI SHAppBarMessage(DWORD,APPBARDATA*);
#define ABM_SETSTATE 10
static BOOL CALLBACK restore(HWND window,LPARAM unused){
 WCHAR name[96];GetClassNameW(window,name,96);
 if(!lstrcmpW(name,L"Shell_TrayWnd")||!lstrcmpW(name,L"Shell_SecondaryTrayWnd"))ShowWindow(window,SW_SHOWNA);
 return TRUE;
}
/* Small independent recovery helper. No polling, keyboard hook, or registry writes. */
int main(int argc,char **argv){
 HANDLE handles[2];APPBARDATA bar;
 if(argc!=4)return 2;
 handles[0]=OpenProcess(SYNCHRONIZE,FALSE,(DWORD)strtoul(argv[1],0,10));
 handles[1]=OpenEventA(SYNCHRONIZE,FALSE,argv[2]);
 if(!handles[0]||!handles[1])return 3;
 if(WaitForMultipleObjects(2,handles,FALSE,INFINITE)==WAIT_OBJECT_0){
  memset(&bar,0,sizeof(bar));bar.cbSize=sizeof(bar);bar.hWnd=FindWindowW(L"Shell_TrayWnd",0);bar.lParam=(LPARAM)strtoul(argv[3],0,10);
  SHAppBarMessage(ABM_SETSTATE,&bar);EnumWindows(restore,0);
 }
 CloseHandle(handles[0]);CloseHandle(handles[1]);return 0;
}
