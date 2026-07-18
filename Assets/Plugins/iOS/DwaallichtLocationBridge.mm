// Native iOS bridge for requesting "Always" authorization and enabling
// background-compatible CLLocationManager updates from Unity.

#import <CoreLocation/CoreLocation.h>
#import <Foundation/Foundation.h>

extern "C" {
    void UnitySendMessage(const char *obj, const char *method, const char *msg);
}

static NSString *const kUnityGameObjectName = @"DwaallichtLocationManager";

@interface DwaallichtLocationDelegate : NSObject <CLLocationManagerDelegate>
@property(nonatomic, strong) CLLocationManager *locationManager;
@end

@implementation DwaallichtLocationDelegate

- (instancetype)init {
    self = [super init];
    if (self) {
        _locationManager = [[CLLocationManager alloc] init];
        _locationManager.delegate = self;
        _locationManager.pausesLocationUpdatesAutomatically = NO;
    }
    return self;
}

- (void)locationManagerDidChangeAuthorization:(CLLocationManager *)manager {
    NSInteger status = manager.authorizationStatus;
    NSString *statusString = [NSString stringWithFormat:@"%ld", (long)status];
    UnitySendMessage([kUnityGameObjectName UTF8String],
                     "OnAuthorizationStatusChanged",
                     [statusString UTF8String]);
}

@end

static DwaallichtLocationDelegate *GetSharedDelegate() {
    static DwaallichtLocationDelegate *sharedDelegate = nil;
    if (sharedDelegate == nil) {
        sharedDelegate = [[DwaallichtLocationDelegate alloc] init];
    }
    return sharedDelegate;
}

extern "C" {

void _Dwaallicht_RequestWhenInUseAuthorization() {
    [GetSharedDelegate().locationManager requestWhenInUseAuthorization];
}

void _Dwaallicht_RequestAlwaysAuthorization() {
    [GetSharedDelegate().locationManager requestAlwaysAuthorization];
}

int _Dwaallicht_GetAuthorizationStatus() {
    return (int)GetSharedDelegate().locationManager.authorizationStatus;
}

void _Dwaallicht_StartBackgroundLocationUpdates() {
    CLLocationManager *manager = GetSharedDelegate().locationManager;
    manager.allowsBackgroundLocationUpdates = YES;
    manager.pausesLocationUpdatesAutomatically = NO;
    [manager startUpdatingLocation];
}

void _Dwaallicht_StopBackgroundLocationUpdates() {
    CLLocationManager *manager = GetSharedDelegate().locationManager;
    [manager stopUpdatingLocation];
    manager.allowsBackgroundLocationUpdates = NO;
}

}
