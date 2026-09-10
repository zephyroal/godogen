// main.cpp — Windows desktop entry (cocos2d-x 4 Win32 path, matching the official template)
#include "main.h"
#include "AppDelegate.h"
#include "cocos2d.h"

USING_NS_CC;

int WINAPI _tWinMain(HINSTANCE hInstance,
                     HINSTANCE hPrevInstance,
                     LPTSTR    lpCmdLine,
                     int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);

    AppDelegate app;
    return Application::getInstance()->run();
}
