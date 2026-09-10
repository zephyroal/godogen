// AppDelegate.cpp — cocos2d-x v4 entry: design resolution, FPS policy, first scene
#include "AppDelegate.h"
#include "GameScene.h"

USING_NS_CC;

AppDelegate::AppDelegate() {}
AppDelegate::~AppDelegate() {}

static Size designResolutionSize = Size(1280, 720);

bool AppDelegate::applicationDidFinishLaunching() {
    auto director = Director::getInstance();
    auto glview = director->getOpenGLView();
    if (!glview) {
#if (CC_TARGET_PLATFORM == CC_PLATFORM_WIN32) || (CC_TARGET_PLATFORM == CC_PLATFORM_MAC)
        glview = GLViewImpl::createWithRect("CocosChess",
            Rect(0, 0, designResolutionSize.width, designResolutionSize.height));
#else
        glview = GLViewImpl::create("CocosChess");
#endif
        director->setOpenGLView(glview);
    }

    director->setAnimationInterval(1.0f / 60.0f);
    glview->setDesignResolutionSize(designResolutionSize.width,
        designResolutionSize.height, ResolutionPolicy::FIXED_HEIGHT);

    auto scene = GameScene::create();
    director->runWithScene(scene);
    return true;
}

void AppDelegate::applicationDidEnterBackground() {
    Director::getInstance()->stopAnimation();
}

void AppDelegate::applicationWillEnterForeground() {
    Director::getInstance()->startAnimation();
}
