// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors
// Native Win32 entry point. No CLR is loaded until the protected child is created.
#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#include <windows.h>
#include <sddl.h>
#include <aclapi.h>
#include <shlobj.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <wchar.h>
#include <stdio.h>

static __typeof__(&BCryptGenRandom) rng;
static __typeof__(&BCryptOpenAlgorithmProvider) hash_open;
static __typeof__(&BCryptCreateHash) hash_create;
static __typeof__(&BCryptHashData) hash_data;
static __typeof__(&BCryptFinishHash) hash_finish;
static __typeof__(&BCryptDestroyHash) hash_destroy;
static __typeof__(&BCryptCloseAlgorithmProvider) hash_close;
#define BCryptGenRandom rng
#define BCryptOpenAlgorithmProvider hash_open
#define BCryptCreateHash hash_create
#define BCryptHashData hash_data
#define BCryptFinishHash hash_finish
#define BCryptDestroyHash hash_destroy
#define BCryptCloseAlgorithmProvider hash_close

// Без PACKAGE_CHECK — запускатель Merno.exe в Program Files\Merno (ставит его Inno, tools/installer/Merno.iss).
// С PACKAGE_CHECK — Merno.Setup.Test.exe для tools/native/smoke.py: только сверяет файлы комплекта с хешами из payload.h.
#ifdef PACKAGE_CHECK
#include "payload.h"
#endif

static WCHAR root[MAX_PATH], self[MAX_PATH];
static PSECURITY_DESCRIPTOR sd;
static SECURITY_ATTRIBUTES sa;
static void fail(const WCHAR *message) { MessageBoxW(NULL,message,L"Merno",MB_OK|MB_ICONERROR); ExitProcess(1); }
static int trusted_sid(PSID sid) {
    PSID installer=NULL;
    int special=ConvertStringSidToSidW(L"S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",&installer)&&EqualSid(sid,installer);
    if(installer) LocalFree(installer);
    return special||IsWellKnownSid(sid,WinBuiltinAdministratorsSid)||IsWellKnownSid(sid,WinLocalSystemSid);
}
static int trusted(const WCHAR *path) {
    DWORD attributes=GetFileAttributesW(path);
    if(attributes==INVALID_FILE_ATTRIBUTES || (attributes&FILE_ATTRIBUTE_REPARSE_POINT)) return 0;
    PSID owner=NULL; PACL acl=NULL; PSECURITY_DESCRIPTOR descriptor=NULL;
    if(GetNamedSecurityInfoW((LPWSTR)path,SE_FILE_OBJECT,OWNER_SECURITY_INFORMATION|DACL_SECURITY_INFORMATION,&owner,NULL,&acl,NULL,&descriptor)!=ERROR_SUCCESS) return 0;
    int ok=trusted_sid(owner) && acl!=NULL;
    for(DWORD i=0;ok && i<acl->AceCount;i++) {
        ACE_HEADER *header=NULL; GetAce(acl,i,(void**)&header);
        if(header->AceFlags&INHERIT_ONLY_ACE) continue;
        if(header->AceType==ACCESS_ALLOWED_ACE_TYPE) {
            ACCESS_ALLOWED_ACE *ace=(ACCESS_ALLOWED_ACE*)header;
            DWORD writes=FILE_WRITE_DATA|FILE_APPEND_DATA|FILE_WRITE_EA|FILE_WRITE_ATTRIBUTES|DELETE|FILE_DELETE_CHILD|WRITE_DAC|WRITE_OWNER|GENERIC_WRITE|GENERIC_ALL;
            if((ace->Mask&writes) && !trusted_sid((PSID)&ace->SidStart)) ok=0;
        } else if(header->AceType!=ACCESS_DENIED_ACE_TYPE) ok=0; // fail closed for unusual ACLs
    }
    LocalFree(descriptor); return ok;
}
static int tree(const WCHAR *path) {
    if(!trusted(path)) return 0;
    WCHAR pattern[MAX_PATH];if(_snwprintf(pattern,MAX_PATH,L"%ls\\*",path)<0)return 0;
    WIN32_FIND_DATAW data;HANDLE search=FindFirstFileW(pattern,&data);if(search==INVALID_HANDLE_VALUE)return 0;
    int ok=1;
    do {
        if(!wcscmp(data.cFileName,L".")||!wcscmp(data.cFileName,L".."))continue;
        WCHAR child[MAX_PATH];if(_snwprintf(child,MAX_PATH,L"%ls\\%ls",path,data.cFileName)<0){ok=0;break;}
        if(!trusted(child)||((data.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)&&!tree(child))){ok=0;break;}
    }while(FindNextFileW(search,&data));
    FindClose(search);return ok;
}
static void join(WCHAR *out,const WCHAR *a,const WCHAR *b) {
    if(_snwprintf(out,MAX_PATH,L"%ls\\%ls",a,b)<0) fail(L"Слишком длинный путь установки.");
}
static void directory(const WCHAR *path) {
    if(!CreateDirectoryW(path,&sa) && GetLastError()!=ERROR_ALREADY_EXISTS) fail(L"Не удалось создать защищённую папку.");
    if(!trusted(path)) fail(L"Папка установки не прошла проверку безопасности.");
}
static void clear_overrides(void) {
    LPWCH block=GetEnvironmentStringsW();
    for(LPWCH item=block;item && *item;item+=wcslen(item)+1) {
        if(!_wcsnicmp(item,L"COR_",4)||!_wcsnicmp(item,L"CORECLR_",8)||!_wcsnicmp(item,L"DOTNET_",7)||!_wcsnicmp(item,L"COMPLUS_",8)) {
            WCHAR name[512]; const WCHAR *equal=wcschr(item,L'=');
            if(equal && equal-item<511) { wcsncpy(name,item,equal-item); name[equal-item]=0; SetEnvironmentVariableW(name,NULL); }
        }
    }
    if(block) FreeEnvironmentStringsW(block);
}
static void environment(void) {
    clear_overrides();
    WCHAR work[MAX_PATH], temp[MAX_PATH], sys[MAX_PATH], modules[2*MAX_PATH];
    join(work,root,L"work"); directory(work);
    unsigned char random[16]; if(BCryptGenRandom(NULL,random,sizeof random,BCRYPT_USE_SYSTEM_PREFERRED_RNG)) fail(L"Не удалось подготовить временную папку.");
    WCHAR name[64]; _snwprintf(name,64,L"native-%lu-%02x%02x%02x%02x%02x%02x%02x%02x",GetCurrentProcessId(),random[0],random[1],random[2],random[3],random[4],random[5],random[6],random[7]);
    join(temp,work,name); directory(temp);
    SetEnvironmentVariableW(L"TEMP",temp); SetEnvironmentVariableW(L"TMP",temp);
    GetSystemDirectoryW(sys,MAX_PATH);
    WCHAR programFiles[MAX_PATH]; SHGetFolderPathW(NULL,CSIDL_PROGRAM_FILES,NULL,SHGFP_TYPE_CURRENT,programFiles);
    _snwprintf(modules,2*MAX_PATH,L"%ls\\WindowsPowerShell\\v1.0\\Modules;%ls\\WindowsPowerShell\\Modules",sys,programFiles);
    SetEnvironmentVariableW(L"PSModulePath",modules);
    SetEnvironmentVariableW(L"DOTNET_EnableDiagnostics",L"0");
    SetEnvironmentVariableW(L"DOTNET_EnableDiagnostics_Profiler",L"0");
    SetDllDirectoryW(L"");
}
static const WCHAR *arguments(void) {
    const WCHAR *p=GetCommandLineW();
    if(*p==L'"') { p++; while(*p && *p!=L'"') p++; if(*p) p++; }
    else while(*p && *p!=L' ') p++;
    while(*p==L' ') p++; return p;
}
static int elevated(void) {
    HANDLE token; TOKEN_ELEVATION value; DWORD size;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return 0;
    int ok=GetTokenInformation(token,TokenElevation,&value,sizeof value,&size)&&value.TokenIsElevated;
    CloseHandle(token); return ok;
}
static int valid_install_name(const char *name) {
    size_t length=strlen(name);
    if(!length||length>63||name[0]=='.'||name[length-1]=='.'||strstr(name,".."))return 0;
    for(size_t i=0;i<length;i++)if((name[i]<'0'||name[i]>'9')&&name[i]!='.')return 0;
    return 1;
}
static void launch(const WCHAR *file,const WCHAR *args,int wait) {
    WCHAR command[32768];
    if(_snwprintf(command,32768,L"\"%ls\" %ls",file,args)<0) fail(L"Слишком длинная команда запуска.");
    STARTUPINFOW startup={0}; startup.cb=sizeof startup; PROCESS_INFORMATION process={0};
    if(!CreateProcessW(file,command,NULL,NULL,FALSE,CREATE_UNICODE_ENVIRONMENT,NULL,root,&startup,&process)) fail(L"Не удалось запустить Merno. Проверьте комплект установки.");
    CloseHandle(process.hThread);
    if(wait) { WaitForSingleObject(process.hProcess,INFINITE); DWORD code=1;GetExitCodeProcess(process.hProcess,&code);CloseHandle(process.hProcess);ExitProcess(code); }
    CloseHandle(process.hProcess);
}
#ifdef PACKAGE_CHECK
static int digest(HANDLE file,const unsigned char *expected) {
    BCRYPT_ALG_HANDLE algorithm=NULL; BCRYPT_HASH_HANDLE hash=NULL; unsigned char result[32],buffer[65536]; DWORD count;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,NULL,0)) return 0;
    if(BCryptCreateHash(algorithm,&hash,NULL,0,NULL,0,0)) { BCryptCloseAlgorithmProvider(algorithm,0);return 0; }
    int ok=1;
    for(;;) {
        if(!ReadFile(file,buffer,sizeof buffer,&count,NULL)) { ok=0;break; }
        if(!count) break;
        if(BCryptHashData(hash,buffer,count,0)) { ok=0;break; }
    }
    if(BCryptFinishHash(hash,result,32,0)) ok=0;
    BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(algorithm,0);
    return ok && !memcmp(result,expected,32);
}
#endif
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,LPWSTR unused,int show) {
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    HMODULE crypto=LoadLibraryExW(L"bcrypt.dll",NULL,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!crypto)return 1;
    rng=(void*)GetProcAddress(crypto,"BCryptGenRandom");hash_open=(void*)GetProcAddress(crypto,"BCryptOpenAlgorithmProvider");
    hash_create=(void*)GetProcAddress(crypto,"BCryptCreateHash");hash_data=(void*)GetProcAddress(crypto,"BCryptHashData");
    hash_finish=(void*)GetProcAddress(crypto,"BCryptFinishHash");hash_destroy=(void*)GetProcAddress(crypto,"BCryptDestroyHash");hash_close=(void*)GetProcAddress(crypto,"BCryptCloseAlgorithmProvider");
    if(!rng||!hash_open||!hash_create||!hash_data||!hash_finish||!hash_destroy||!hash_close)return 1;
    clear_overrides();
    if(!wcscmp(arguments(),L"--environment-self-test")) {
        WCHAR value[2];
        return GetEnvironmentVariableW(L"DOTNET_STARTUP_HOOKS",value,2)||GetEnvironmentVariableW(L"CORECLR_ENABLE_PROFILING",value,2)||GetEnvironmentVariableW(L"COR_ENABLE_PROFILING",value,2)?1:0;
    }
    if(!GetModuleFileNameW(NULL,self,MAX_PATH)||FAILED(SHGetFolderPathW(NULL,CSIDL_PROGRAM_FILES,NULL,SHGFP_TYPE_CURRENT,root))) return 1;
#ifdef PACKAGE_CHECK
    if(!wcscmp(arguments(),L"--validate-package")) {
        WCHAR source[MAX_PATH];wcscpy(source,self);*wcsrchr(source,L'\\')=0;
        for(unsigned int i=0;i<PAYLOAD_COUNT;i++) {
            WCHAR payload[MAX_PATH],file[MAX_PATH];join(payload,source,L"payload");join(file,payload,payload_files[i].path);
            HANDLE input=CreateFileW(file,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,0,NULL);
            if(input==INVALID_HANDLE_VALUE)return 1000+i;
            int valid=digest(input,payload_files[i].hash);CloseHandle(input);if(!valid)return 2000+i;
        }
        return 0;
    }
    return 1; // проверочная сборка ничего не ставит и прав не просит
#else
    WCHAR programFiles[MAX_PATH];wcscpy(programFiles,root);join(root,programFiles,L"Merno");
    WCHAR expected[MAX_PATH];join(expected,root,L"Merno.exe");
    if(_wcsicmp(self,expected)||!trusted(programFiles)||!trusted(root)||!trusted(self)) fail(L"Запускайте установленный Merno.exe из Program Files\\Merno.");
    if(!elevated()) {
        SHELLEXECUTEINFOW info={0};info.cbSize=sizeof info;info.lpVerb=L"runas";info.lpFile=self;info.lpParameters=arguments();info.nShow=SW_SHOW;
        return ShellExecuteExW(&info)?0:1;
    }
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)(A;OICI;RC;;;OW)",SDDL_REVISION_1,&sd,NULL)) return 1;
    sa.nLength=sizeof sa;sa.lpSecurityDescriptor=sd;sa.bInheritHandle=FALSE;
    // после повышения прав — ещё раз: между проверкой и запуском файлы могли подменить
    if(_wcsicmp(self,expected)||!trusted(programFiles)||!trusted(root)||!trusted(self)) fail(L"Запускайте установленный Merno.exe из Program Files\\Merno.");
    WCHAR current[MAX_PATH];join(current,root,L"current.txt");if(!trusted(current)) fail(L"Номер версии не прошёл проверку.");
    HANDLE file=CreateFileW(current,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,0,NULL);char version[65]={0};DWORD count;
    if(file==INVALID_HANDLE_VALUE||!ReadFile(file,version,sizeof version-1,&count,NULL)||count==0) fail(L"Не найдена установленная версия.");CloseHandle(file);
    if(count!=strlen(version)||!valid_install_name(version)) fail(L"Неверный номер установленной версии.");
    WCHAR relative[96],child[MAX_PATH],versionDirectory[MAX_PATH];_snwprintf(relative,96,L"versions\\%hs",version);join(versionDirectory,root,relative);
    WCHAR versions[MAX_PATH];join(versions,root,L"versions");
    join(child,versionDirectory,L"Merno.exe");if(!trusted(versions)||!tree(versionDirectory)) fail(L"Файлы установленной версии не прошли проверку.");
    environment();launch(child,arguments(),1);return 0;
#endif
}
