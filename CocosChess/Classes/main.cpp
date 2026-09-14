// main.cpp — Windows desktop entry (cocos2d-x 4 Win32 path, matching the official template)
#include "main.h"
#include "AppDelegate.h"
#include "GameScene.h"
#include "cocos2d.h"

#include <string.h>
#include <wchar.h>

USING_NS_CC;

int WINAPI _tWinMain(HINSTANCE hInstance,
                     HINSTANCE hPrevInstance,
                     LPTSTR    lpCmdLine,
                     int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(nCmdShow);

    // LAN self-test hooks: launch two instances with --net-host / --net-join
    // and they connect over 127.0.0.1 without any touch input (this verifies
    // the host/join/move-sync path end-to-end on one machine).
#ifdef _UNICODE
    if (lpCmdLine && wcsstr(lpCmdLine, L"--net-host"))
        GameScene::SetNetTestFlags(true, false, "host");
    else if (lpCmdLine && wcsstr(lpCmdLine, L"--net-join"))
        GameScene::SetNetTestFlags(false, true, "guest");
    if (lpCmdLine && wcsstr(lpCmdLine, L"--verify"))
        GameScene::SetVerifyFlow(true);
#else
    if (lpCmdLine && strstr(lpCmdLine, "--net-host"))
        GameScene::SetNetTestFlags(true, false, "host");
    else if (lpCmdLine && strstr(lpCmdLine, "--net-join"))
        GameScene::SetNetTestFlags(false, true, "guest");
    if (lpCmdLine && strstr(lpCmdLine, "--verify"))
        GameScene::SetVerifyFlow(true);
#endif

    AppDelegate app;
    return Application::getInstance()->run();
}
