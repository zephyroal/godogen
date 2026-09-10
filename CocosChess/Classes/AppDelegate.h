// AppDelegate.h — cocos2d-x application entry point (v4)
#pragma once
#include "cocos2d.h"

class AppDelegate : public cocos2d::Application {
public:
    AppDelegate();
    virtual ~AppDelegate();

    virtual bool applicationDidFinishLaunching() override;
    virtual void applicationDidEnterBackground() override;
    virtual void applicationWillEnterForeground() override;
};
