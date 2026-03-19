#import <Foundation/Foundation.h>
#import "StoreKitWrapper.h"

#pragma mark - Internal Category for Swift Transaction Extensions

@interface SKXPaymentTransaction (Internal)
@property (nonatomic) BOOL isDummyTransaction;
@property (nonatomic) BOOL isRestoredTransaction;
@property (nonatomic) BOOL wasPaystationOpened;
@property (nonatomic, copy, nullable) NSString *paystationToken;
@end

#pragma mark XsollaMobileSDK Unity

typedef void (^XsollaJsonCallbackDelegate)(NSString *jsonResult, NSString *error);
typedef void (*XsollaUnityBridgeJsonCallbackDelegate)(int64_t callbackData, const char *jsonResult, const char *error);

// Error code string constants to keep parity with C# enum names
static NSString* const kXsollaPurchaseErrorCodeUnknown = @"Unknown";
static NSString* const kXsollaPurchaseErrorCodeCancelled = @"Cancelled";
static NSString* const kXsollaPurchaseErrorCodeAborted = @"Aborted";
static NSString* const kXsollaPurchaseErrorCodeInternal = @"Internal";

// Minimal logging wrapper to respect configured log level.
static SKLogLevel gXsollaLogLevel = SKLogLevelWarning;
static void XsollaUnityLogv(SKLogLevel currentLevel, SKLogLevel level, NSString *format, va_list args) {
    if (level < currentLevel) {
        return;
    }
    NSString *message = [[NSString alloc] initWithFormat:format arguments:args];
    NSLog(@"%@", message);
}

static void XsollaUnityLogWithOverride(SKLogLevel currentLevel, SKLogLevel level, NSString *format, ...) {
    va_list args;
    va_start(args, format);
    XsollaUnityLogv(currentLevel, level, format, args);
    va_end(args);
}

static void XsollaUnityLog(SKLogLevel level, NSString *format, ...) {
    va_list args;
    va_start(args, format);
    XsollaUnityLogv(gXsollaLogLevel, level, format, args);
    va_end(args);
}

static NSDictionary* XsollaJsonDictionaryFromCString(const char* jsonCStr) {
    if (!jsonCStr) {
        return nil;
    }
    NSString *jsonString = [NSString stringWithUTF8String:jsonCStr];
    NSData *data = [jsonString dataUsingEncoding:NSUTF8StringEncoding];
    if (!data) {
        return nil;
    }
    NSDictionary* json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    return json;
}

static SKLogLevel XsollaParseLogLevelFromDictionary(NSDictionary* json) {
    SKLogLevel level = SKLogLevelWarning;
    if (!json) {
        return level;
    }
    
    NSString* logLevel = json[@"logLevel"];
    
    if (logLevel && [logLevel length] > 0) {
        if ([logLevel isEqualToString:@"Debug"])
            level = SKLogLevelVerbose;
        else if ([logLevel isEqualToString:@"Warning"])
            level = SKLogLevelWarning;
        else if ([logLevel isEqualToString:@"Error"] || [logLevel isEqualToString:@"None"])
            level = SKLogLevelError;
        else {
            // Use default warning threshold to report unsupported value.
            XsollaUnityLogWithOverride(SKLogLevelWarning, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported logLevel value: %@", logLevel);
        }
    }
    
    return level;
}

static SKLogLevel XsollaParseLogLevelFromCString(const char* jsonCStr) {
    NSDictionary* json = XsollaJsonDictionaryFromCString(jsonCStr);
    return XsollaParseLogLevelFromDictionary(json);
}

@interface XsollaUnityMobile : NSObject <SKPaymentTransactionObserver, SKProductsRequestDelegate>
@property (nonatomic,strong,nonnull) NSMutableArray<SKProduct*>* products;

@property (nonatomic,strong,nullable) NSMutableDictionary<NSValue*, XsollaJsonCallbackDelegate>* productsCallbacks;
@property (nonatomic,strong,nullable) NSMutableDictionary<NSValue*, XsollaJsonCallbackDelegate>* purchaseCallbacks;
@property (nonatomic,strong,nullable) NSMutableDictionary<NSString*, XsollaJsonCallbackDelegate>* purchaseDummyCallbacks;
@property (nonatomic,strong,nullable) SKPaymentSettings* settings;
@property (nonatomic,copy,nullable) XsollaJsonCallbackDelegate defaultPurchaseCallback;
@property (nonatomic,assign,nullable) XsollaUnityBridgeJsonCallbackDelegate paymentEventCallback;
@property (nonatomic) int64_t paymentEventCallbackData;

@property (nonatomic,strong,nonnull) NSMutableArray<void (^)(void)>* pendingProductRequests;
@property (nonatomic) BOOL requiresLoginForPersonalizedProducts;
@property (nonatomic) BOOL isLoggedInToPaymentQueue;


@property (nonatomic) BOOL isSandbox;
@property (nonatomic) BOOL useWebShop;

@end

@implementation XsollaUnityMobile
static XsollaUnityMobile *sharedMyManager = nil;

+ (instancetype) shared {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        sharedMyManager = [[XsollaUnityMobile alloc] init];
    });
    return sharedMyManager;
}

+ (void) destroy {
    [sharedMyManager stop];
}

-(id)init {
    if ((self = [super init])) {
        [self reset];
    }
    return self;
}

- (void) initialize:(SKPaymentSettings * _Nonnull)settings withCallback:(void(^)(NSString*, NSString*))callback {
    XsollaUnityLog(SKLogLevelVerbose, @"XsollaUnityMobile initialize");
    
    [self reset];

    SKPaymentQueue* queue = [SKPaymentQueue defaultQueue];
    [queue startWithSettings:settings]; 
    [queue addTransactionObserver: self];

    self.isLoggedInToPaymentQueue = settings.customLoginToken != nil && [settings.customLoginToken length] > 0;
    self.requiresLoginForPersonalizedProducts = settings.fetchPersonalizedProducts && !self.isLoggedInToPaymentQueue;
    self.isSandbox = settings.useSandbox;
    self.settings = settings;
    
    callback(@"{}", nil);
}

-(void) stop {
    [[SKPaymentQueue defaultQueue] removeTransactionObserver: self];
}

-(void) reset {
    self.products = [NSMutableArray arrayWithCapacity:10];
    self.productsCallbacks = [NSMutableDictionary dictionaryWithCapacity:10];
    self.purchaseCallbacks = [NSMutableDictionary dictionaryWithCapacity:10];
    self.purchaseDummyCallbacks = [NSMutableDictionary dictionaryWithCapacity:10];
    self.pendingProductRequests = [NSMutableArray arrayWithCapacity:2];
    self.requiresLoginForPersonalizedProducts = NO;
    self.isLoggedInToPaymentQueue = NO;
    self.isSandbox = YES;
    self.useWebShop = NO;
    self.settings = nil;
    self.paymentEventCallback = nil;
    self.paymentEventCallbackData = 0;
}

- (NSString*) errorJsonWithMessage:(NSString*)message code:(NSString*)code {
    NSError *err;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:@{ @"message": message ?: @"", @"code": code ?: kXsollaPurchaseErrorCodeUnknown } options:0 error:&err];
    if (jsonData != nil) {
        return [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
    }
    return [NSString stringWithFormat:@"{ \"message\": \"%@\", \"code\": \"%@\" }", message ?: @"", code ?: kXsollaPurchaseErrorCodeUnknown];
}

- (NSString*) mapNSErrorToCode:(NSError*)error {
    if (!error) return kXsollaPurchaseErrorCodeUnknown;
    switch (error.code) {
        case SKErrorUnknown:
            return kXsollaPurchaseErrorCodeUnknown;
        case SKErrorPaymentCancelled:
            return kXsollaPurchaseErrorCodeCancelled;
        case SKErrorClientInvalid:
        case SKErrorPaymentInvalid:
//        case SKErrorPaymentNotAllowed:
//        case SKErrorStoreProductNotAvailable:
//        case SKErrorCloudServiceNetworkConnectionFailed:
//        case SKErrorCloudServicePermissionDenied:
//        case SKErrorCloudServiceRevoked:
//        case SKErrorOverlayTimeout:
//        case SKErrorOverlayConnectionFailed:
//        case SKErrorPrivacyAcknowledgementRequired:
//        case SKErrorUnauthorizedRequestData:
//        case SKErrorMissingOfferParams:
//        case SKErrorInvalidOfferPrice:
            return kXsollaPurchaseErrorCodeInternal;
        case SKErrorAborted:
            return kXsollaPurchaseErrorCodeAborted;
        default:
            return kXsollaPurchaseErrorCodeUnknown;
    }
}

-(SKProduct* _Nullable) productBySku:(NSString*)sku {
    for (SKProduct* p in _products) {
        if ([p.productIdentifier isEqualToString:sku]) {
            return p;
        }
    }
    
    return nil;
}

#pragma mark To Json Helpers

NSString* paymentToJson(SKPaymentTransaction* transaction) {
    NSData* receipt = transaction.transactionReceipt;

    NSMutableDictionary* dict = [[NSMutableDictionary alloc] init];
    dict[@"sku"] = transaction.payment.productIdentifier;
    dict[@"status"] = @"purchased";
    dict[@"transactionId"] = transaction.transactionIdentifier ? transaction.transactionIdentifier : @"";
    dict[@"orderId"] = transaction.transactionIdentifier ? transaction.transactionIdentifier : @"";
    dict[@"invoiceId"] = transaction.invoiceIdentifier ? transaction.invoiceIdentifier : @"";
    
     if (receipt) {
         NSString* receiptString = [receipt base64EncodedStringWithOptions:0];
         dict[@"receipt"] = receiptString ?: @"";
     } else {
         dict[@"receipt"] = @"";
     }
    
    NSError *error;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:dict
                               options:NSJSONWritingPrettyPrinted // Pass 0 if you don't care about the readability of the generated string
                               error:&error];

    NSString *jsonString = nil;
    if (jsonData == nil) {
        XsollaUnityLog(SKLogLevelError, @"Got an error: %@", error);
        return nil;
    } else {
        jsonString = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
    }
    
    return [NSString stringWithFormat:@"{ purchases: [%@] }", jsonString] ;
}

NSString* productToJson(NSArray<SKXProduct *>* products) {
    NSMutableArray* productsArray = [NSMutableArray arrayWithCapacity:products.count];
    
    for (SKProduct* p in products) {
        NSMutableDictionary* dict = [[NSMutableDictionary alloc] init];
        
        dict[@"sku"] = p.productIdentifier;
        dict[@"title"] = p.localizedTitle;
        dict[@"description"] = p.localizedDescription;
        dict[@"price"] = [NSNumber numberWithLong:p.price.doubleValue * 1e6];
        dict[@"currency"] = p.priceLocale && p.priceLocale.currencyCode ? p.priceLocale.currencyCode : @"USD";
        dict[@"formattedPrice"] = p.displayPrice;
        dict[@"iconUrl"] = p.imageUrl && p.imageUrl.absoluteString ? p.imageUrl.absoluteString : @"";

        dict[@"priceWithoutDiscount"] = [NSNumber numberWithLong:p.price.doubleValue * 1e6];
        dict[@"formattedPriceWithoutDiscount"] = p.displayPrice;

        if (p.introductoryPrice) {
            dict[@"priceWithDiscount"] = [NSNumber numberWithLong:p.introductoryPrice.price.doubleValue * 1e6];
            dict[@"formattedPriceWithDiscount"] = p.introductoryPrice.displayPrice;
        }
        
        [productsArray addObject:dict];
    }
    
    NSError *error;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:productsArray
                               options:NSJSONWritingPrettyPrinted // Pass 0 if you don't care about the readability of the generated string
                               error:&error];

    NSString *jsonString = nil;
    if (jsonData == nil) {
        XsollaUnityLog(SKLogLevelError, @"Got an error: %@", error);
        return nil;
    } else {
        jsonString = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
    }
    
    return [NSString stringWithFormat:@"{ products: %@ }", jsonString] ;
}

+ (UIColor *)colorWithHexString:(NSString *)stringToConvert
{
    NSString *noHashString = [stringToConvert stringByReplacingOccurrencesOfString:@"#" withString:@""]; // remove the #
    NSScanner *scanner = [NSScanner scannerWithString:noHashString];
    [scanner setCharactersToBeSkipped:[NSCharacterSet symbolCharacterSet]]; // remove + and $

    unsigned hex;
    if (![scanner scanHexInt:&hex]) return nil;
    
    if (hex == 0) return nil;
    
    int r = (hex >> 24) & 0xFF;
    int g = (hex >> 16) & 0xFF;
    int b = (hex >> 8) & 0xFF;
    int a = (hex) & 0xFF;

    return [UIColor colorWithRed:r / 255.0f green:g / 255.0f blue:b / 255.0f alpha:a / 255.0f];
}

#pragma mark - SKPaymentTransactionObserver

- (void) paymentQueue: (SKPaymentQueue *)queue updatedTransactions: (NSArray<SKPaymentTransaction *> *)transactions {
    XsollaUnityLog(SKLogLevelVerbose, @"XsollaUnityMobile paymentQueue updatedTransactions");
    
    for (SKPaymentTransaction* t in transactions) {
        BOOL ended = NO;
        
        XsollaJsonCallbackDelegate callback = [self.purchaseCallbacks objectForKey:[NSValue valueWithPointer:(const void*)t.payment]];
        
        if (callback == nil && t.isDummyTransaction && t.paystationToken) {
            callback = [self.purchaseDummyCallbacks objectForKey:t.paystationToken];
        }
        
        if (callback == nil) {
            callback = self.defaultPurchaseCallback;
        }
        
        switch (t.transactionState) {
            case SKPaymentTransactionStatePurchased:
            {
#if DEBUG
                if (t.transactionIdentifier) {
                    BOOL exists = NO;
                    
                    for (SKPaymentTransaction* o in [SKPaymentQueue defaultQueue].transactions) {
                        if (o.transactionIdentifier
                            && [o.transactionIdentifier isEqualToString:t.transactionIdentifier]
                            && o.transactionState == SKPaymentTransactionStatePurchased) {
                            exists = YES;
                            break;
                        }
                    }
                    
                    assert(exists == YES);
                }
#endif
                // Send PaystationCompleted event before calling purchase callback, if it was opened before
                if (t.wasPaystationOpened) {
                    _SendPaystationCompletedEvent(self);
                }
                
                NSString* result = paymentToJson(t);
                if (result == nil) {
                    NSString* json = [self errorJsonWithMessage:@"Failed to serialize payment" code:kXsollaPurchaseErrorCodeInternal];
                    callback(nil, json);
                } else {
                    callback(result, nil);
                }
                ended = YES;
                break;
            }
            case SKPaymentTransactionStateFailed:
            {
                // Send PaystationCompleted event before calling purchase callback, if it was opened before
                if (t.wasPaystationOpened) {
                    _SendPaystationCompletedEvent(self);
                }
                
                NSString* msg = t.error ? [t.error description] : @"Failed to buy";
                NSString* code = [self mapNSErrorToCode:t.error];
                NSString* json = [self errorJsonWithMessage:msg code:code];
                callback(nil, json);
                ended = YES;
                break;
            }
            default:
                // nothing prolly
                break;
        }
        
        if (ended) {
            [self.purchaseCallbacks removeObjectForKey:[NSValue valueWithPointer:(const void*)t.payment]];
            
            if (t.isDummyTransaction && t.paystationToken) {
                [self.purchaseDummyCallbacks removeObjectForKey:t.paystationToken];
            }
        }
    }
}

- (void) paymentQueue: (SKPaymentQueue *)queue removedTransactions: (NSArray<SKPaymentTransaction *> *)transactions {
    XsollaUnityLog(SKLogLevelVerbose, @"XsollaUnityMobile paymentQueue removedTransactions");
}

// Helper function to convert payment event enum to JSON string
static NSString* _PaymentEventToJson(enum SKXPaymentTransactionPaystationEvent event) {
    NSString* eventType = nil;
    
    switch (event) {
        case SKXPaymentTransactionPaystationEventWillOpen:
            eventType = @"WillOpen";
            break;
        case SKXPaymentTransactionPaystationEventOpened:
            eventType = @"Opened";
            break;
        case SKXPaymentTransactionPaystationEventLoaded:
            eventType = @"Loaded";
            break;
        case SKXPaymentTransactionPaystationEventWillClose:
            eventType = @"WillClose";
            break;
        case SKXPaymentTransactionPaystationEventClosed:
            eventType = @"Closed";
            break;
        case SKXPaymentTransactionPaystationEventOpenedExternally:
            eventType = @"OpenedExternally";
            break;
        default:
            XsollaUnityLog(SKLogLevelWarning, @"Unknown payment event: %ld", (long)event);
            return nil;
    }
    
    NSDictionary* eventDict = @{
        @"type": eventType,
        @"payloadJson": @""
    };
    
    NSError* error = nil;
    NSData* jsonData = [NSJSONSerialization dataWithJSONObject:eventDict options:0 error:&error];
    if (error != nil) {
        XsollaUnityLog(SKLogLevelError, @"Failed to serialize payment event to JSON: %@", error);
        return nil;
    }
    
    return [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
}

// Helper function to send "Completed" payment event to Unity
static void _SendPaystationCompletedEvent(XsollaUnityMobile* instance) {
    if (instance.paymentEventCallback != NULL) {
        NSDictionary* eventDict = @{
            @"type": @"Completed",
            @"payloadJson": @""
        };
        
        NSError* error = nil;
        NSData* jsonData = [NSJSONSerialization dataWithJSONObject:eventDict options:0 error:&error];
        if (error != nil) {
            XsollaUnityLog(SKLogLevelError, @"Failed to serialize Completed event to JSON: %@", error);
            return;
        }
        
        NSString* eventJson = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
        (*instance.paymentEventCallback)(instance.paymentEventCallbackData, [eventJson UTF8String], NULL);
    }
}

- (void)paymentQueue:(SKXPaymentQueue * _Nonnull)queue event:(enum SKXPaymentTransactionPaystationEvent)event for:(SKXPaymentTransaction * _Nonnull)transaction {
    XsollaUnityLog(SKLogLevelVerbose, @"XsollaUnityMobile paymentQueue event: %ld for: %@", (long)event, transaction);
    
    // Send event to Unity if callback is set
    if (self.paymentEventCallback != NULL) {
        NSString* eventJson = _PaymentEventToJson(event);
        if (eventJson != nil) {
            (*self.paymentEventCallback)(self.paymentEventCallbackData, [eventJson UTF8String], NULL);
        }
    }
}

- (void)paymentQueueDidLogin:(SKXPaymentQueue *)queue {
    XsollaUnityLog(SKLogLevelVerbose, @"XsollaUnityMobile paymentQueueDidLogin");
    self.isLoggedInToPaymentQueue = YES;

    if (self.pendingProductRequests.count == 0) {
        return;
    }

    NSArray<void (^)(void)> *pending = [self.pendingProductRequests copy];
    [self.pendingProductRequests removeAllObjects];

    for (void (^startBlock)(void) in pending) {
        if (startBlock) {
            startBlock();
        }
    }
}

#pragma mark - SKProductsRequestDelegate

-(void) productsRequest:(SKProductsRequest*)request didReceiveResponse:(SKProductsResponse*)response {
    XsollaJsonCallbackDelegate callback = [self.productsCallbacks objectForKey: [NSValue valueWithPointer:(const void*)request]];
    
    if (callback == nil) {
        return;
    }
    
    [self.productsCallbacks removeObjectForKey: [NSValue valueWithPointer:(const void*)request]];
    
    for (SKProduct* p in response.products) {
        [_products addObject:p]; // local cache
    }
    
    XsollaUnityLog(SKLogLevelWarning, @"XsollaUnityMobile productsRequest didReceiveResponse, some product identifiers were invalid: %@", response.invalidProductIdentifiers);
    
    NSString* result = productToJson(response.products);
    if (result == nil) {
        callback(nil, @"Failed to serialize products");
    } else {
        callback(result, nil);
    }
}

-(void) request:(SKRequest*)request didFailWithError:(NSError*)error {
    XsollaJsonCallbackDelegate callback = [self.productsCallbacks objectForKey: [NSValue valueWithPointer:(const void*)request]];

    if (callback == nil) {
        return;
    }
    
    callback(nil, error ? error.description : @"Failed to request products");
}

@end

#pragma mark From Json Helpers

// Log any keys that were present in a dictionary but were not explicitly handled.
static void XsollaLogUnusedKeys(NSDictionary *dict, NSSet<NSString *> *knownKeys, NSString *context, SKLogLevel currentLevel) {
    if (dict == nil || dict.count == 0 || knownKeys == nil) {
        return;
    }
    
    NSMutableSet<NSString *> *unknownKeys = [NSMutableSet setWithArray:dict.allKeys];
    [unknownKeys minusSet:knownKeys];
    
    if (unknownKeys.count > 0) {
        XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unused keys in %@: %@", context, [[unknownKeys allObjects] componentsJoinedByString:@", "]);
    }
}

#pragma mark Retry Policies Parsing

// Parse a retry profile from JSON dictionary and create XMRetryPolicy object
static XMRetryPolicy* _ParseRetryProfile(NSDictionary* json, SKLogLevel currentLevel) {
    if (json == nil) {
        return nil;
    }
    
    NSString* type = json[@"type"];
    if (type == nil || type.length == 0) {
        XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"Failed to parse retry profile: missing type");
        return nil;
    }
    
    NSNumber* maxNumAttempts = json[@"maxNumAttempts"];
    if (maxNumAttempts == nil) {
        XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"Failed to parse retry profile: missing maxNumAttempts");
        return nil;
    }
    
    if ([type caseInsensitiveCompare:@"Uniform"] == NSOrderedSame) {
        NSNumber* intervalMillis = json[@"intervalMillis"];
        if (intervalMillis == nil) {
            XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"Failed to parse uniform retry profile: missing intervalMillis");
            return nil;
        }
        return [[XMRetryPolicy alloc] initWithUniform:[maxNumAttempts integerValue] intervalMillis:[intervalMillis integerValue]];
    } else if ([type caseInsensitiveCompare:@"ExponentialBackoff"] == NSOrderedSame) {
        NSNumber* baseIntervalMillis = json[@"baseIntervalMillis"];
        if (baseIntervalMillis == nil) {
            XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"Failed to parse exponential backoff retry profile: missing baseIntervalMillis");
            return nil;
        }
        NSNumber* maxIntervalMillis = json[@"maxIntervalMillis"];
        NSNumber* maxRandomExtraDelayMillis = json[@"maxRandomExtraDelayMillis"];
        return [[XMRetryPolicy alloc] initWithExponentialBackoff:[maxNumAttempts integerValue]
                                               baseIntervalMillis:[baseIntervalMillis integerValue]
                                                maxIntervalMillis:maxIntervalMillis
                                        maxRandomExtraDelayMillis:maxRandomExtraDelayMillis];
    } else {
        XsollaUnityLogWithOverride(currentLevel, SKLogLevelWarning, @"Failed to parse retry profile: unknown type '%@'", type);
        return nil;
    }
}

// Parse RetryPolicies from JSON dictionary and create XMRetryPolicies object
static XMRetryPolicies* _ParseRetryPolicies(NSDictionary* json, SKLogLevel currentLevel) {
    if (json == nil || json.count == 0) {
        return nil;
    }
    
    NSDictionary* retryPoliciesJson = json[@"retryPolicies"];
    if (retryPoliciesJson == nil || retryPoliciesJson.count == 0) {
        return nil;
    }
    
    XMRetryPolicies* policies = [[XMRetryPolicies alloc] init];
    BOOL hasAnyProfile = NO;
    
    NSDictionary* defaultProfile = retryPoliciesJson[@"defaultRetryProfileOverride"];
    if (defaultProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(defaultProfile, currentLevel);
        if (parsed != nil) {
            policies.defaultRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* pendingOrderProfile = retryPoliciesJson[@"pendingOrderStatusQueryRetryProfileOverride"];
    if (pendingOrderProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(pendingOrderProfile, currentLevel);
        if (parsed != nil) {
            policies.pendingOrderStatusQueryRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* createOrderProfile = retryPoliciesJson[@"createOrderRetryProfileOverride"];
    if (createOrderProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(createOrderProfile, currentLevel);
        if (parsed != nil) {
            policies.createOrderRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* authenticateProfile = retryPoliciesJson[@"authenticateRetryProfileOverride"];
    if (authenticateProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(authenticateProfile, currentLevel);
        if (parsed != nil) {
            policies.authenticateRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* queryProductsProfile = retryPoliciesJson[@"queryProductsRetryProfileOverride"];
    if (queryProductsProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(queryProductsProfile, currentLevel);
        if (parsed != nil) {
            policies.queryProductsRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* queryPurchasesProfile = retryPoliciesJson[@"queryPurchasesRetryProfileOverride"];
    if (queryPurchasesProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(queryPurchasesProfile, currentLevel);
        if (parsed != nil) {
            policies.queryPurchasesRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    NSDictionary* consumePurchasesProfile = retryPoliciesJson[@"consumePurchasesRetryProfileOverride"];
    if (consumePurchasesProfile != nil) {
        XMRetryPolicy* parsed = _ParseRetryProfile(consumePurchasesProfile, currentLevel);
        if (parsed != nil) {
            policies.consumePurchasesRetryProfileOverride = parsed;
            hasAnyProfile = YES;
        }
    }
    
    if (!hasAnyProfile) {
        return nil;
    }
    
    return policies;
}

// Apply retry policies to SKPaymentSettings
static void _ApplyRetryPoliciesToSettings(const char* additionalSettingsJson, SKPaymentSettings* settings, SKLogLevel currentLevel) {
    if (additionalSettingsJson == NULL || strlen(additionalSettingsJson) == 0) {
        return;
    }
    
    NSString *jsonString = [NSString stringWithUTF8String:additionalSettingsJson];
    NSData *data = [jsonString dataUsingEncoding:NSUTF8StringEncoding];
    NSDictionary* json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    
    if (json == nil || json.count == 0) {
        return;
    }
    
    XMRetryPolicies* policies = _ParseRetryPolicies(json, currentLevel);
    if (policies == nil) {
        return;
    }
    
    XsollaUnityLogWithOverride(currentLevel, SKLogLevelVerbose, @"Applying retry policies to SKPaymentSettings");
    settings.retryPolicies = policies;
}

static SKPaymentSettings* _JsonToPaymentSettingsWithLogLevel(const char* jsonCStr, SKLogLevel currentLogLevel) {
    XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeJsonToConfig");
    
    NSString *jsonString = [NSString stringWithUTF8String:jsonCStr];
    NSData *data = [jsonString dataUsingEncoding:NSUTF8StringEncoding];
    NSDictionary* json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    NSDictionary* jsonSettings = json[@"settings"];
    NSDictionary* uiSettings = jsonSettings[@"uiSettings"];
    NSDictionary* redirectSettings = jsonSettings[@"redirectSettings"];
    
    if (json == nil || jsonSettings == nil || uiSettings == nil || redirectSettings == nil
        || jsonSettings[@"projectId"] == nil || jsonSettings[@"loginId"] == nil) {
        return nil;
    }
    
    NSInteger projectId = [jsonSettings[@"projectId"] integerValue];
    NSString* loginId = jsonSettings[@"loginId"];

    SKPaystationSize size = SKPaystationSizeMedium;
    SKPaystationTheme theme = SKPaystationThemeAuto;

    if (uiSettings[@"themeSize"] && [uiSettings[@"themeSize"] length] > 0) {
        NSString* themeSize = uiSettings[@"themeSize"];

        if ([themeSize isEqualToString:@"Small"])
            size = SKPaystationSizeSmall;
        else if ([themeSize isEqualToString:@"Medium"])
            size = SKPaystationSizeMedium;
        else if ([themeSize isEqualToString:@"Large"])
            size = SKPaystationSizeLarge;
        else if (![themeSize isEqualToString:@"Auto"]) {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported themeSize value: %@", themeSize);
        }
    }

    if (uiSettings[@"themeStyle"] && [uiSettings[@"themeStyle"] length] > 0) {
        NSString* themeStyle = uiSettings[@"themeStyle"];

        if ([themeStyle isEqualToString:@"Light"])
            theme = SKPaystationThemeLight;
        else if ([themeStyle isEqualToString:@"Dark"])
            theme = SKPaystationThemeDark;
        else if ([themeStyle isEqualToString:@"Custom"])
            theme = SKPaystationThemeOther;
        else if (![themeStyle isEqualToString:@"Auto"]) {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported themeStyle value: %@", themeStyle);
        }
    }
    
    SKPaymentSettings* settings = [[SKPaymentSettings alloc] initWithProjectId:projectId loginProjectId:loginId platform:SKPaymentPlatformStandalone paystationUIThemeId:theme paystationUISize:size oAuth2ClientId:0];
    
    if (jsonSettings[@"oauthClientId"]) {
        settings.oAuth2ClientId = [jsonSettings[@"oauthClientId"] integerValue];
    }
    
    if (uiSettings[@"customTheme"] != nil && [uiSettings[@"customTheme"] length] > 0) {
        settings.paystationUIThemeCustomId = uiSettings[@"customTheme"];
    }
    
    if (json[@"accessToken"] != nil && [json[@"accessToken"] length] > 0) {
        settings.customLoginToken = json[@"accessToken"];
    }
    
    if (json[@"sandbox"]) {
        settings.useSandbox = [json[@"sandbox"] boolValue];
    }
    
    settings.logLevel = currentLogLevel;
    
    if (jsonSettings[@"localPurchasesRestore"]) {
        settings.useLocalPurchaseRestoration = [jsonSettings[@"localPurchasesRestore"] boolValue];
    }
    
    if (jsonSettings[@"fetchPersonalizedProductsOnly"]) {
        settings.fetchPersonalizedProducts = [jsonSettings[@"fetchPersonalizedProductsOnly"] boolValue];
    }
    
    if (uiSettings[@"controlTintColor"] != nil && [uiSettings[@"controlTintColor"] length] > 0) {
        UIColor* color = [XsollaUnityMobile colorWithHexString:uiSettings[@"controlTintColor"]];
        
        if (color)
            settings.webViewControlTintColor = color;
    }
    
    if (uiSettings[@"barTintColor"] != nil && [uiSettings[@"barTintColor"] length] > 0) {
        UIColor* color = [XsollaUnityMobile colorWithHexString:uiSettings[@"barTintColor"]];
        
        if (color)
            settings.webViewBarTintColor = color;
    }
    
    if (uiSettings[@"visibleLogo"] && [uiSettings[@"visibleLogo"] length] > 0) {
        NSString* visibleLogo = uiSettings[@"visibleLogo"];
        
        // Unity uses VisibleLogo; map to iOS visiblePaymentLogoValue.
        if ([visibleLogo isEqualToString:@"Show"]) {
            settings.visiblePaymentLogoValue = @YES;
        } else if ([visibleLogo isEqualToString:@"Hide"]) {
            settings.visiblePaymentLogoValue = @NO;
        }
    }

    if (redirectSettings[@"redirectButtonText"] != nil && [redirectSettings[@"redirectButtonText"] length] > 0) {
        settings.redirectButtonCaption = redirectSettings[@"redirectButtonText"];
    }
    
    if (redirectSettings[@"redirectDelay"]) {
        settings.redirectTimeOut = [redirectSettings[@"redirectDelay"] integerValue];
    }
    
    if (redirectSettings[@"redirectUrl"] != nil && [redirectSettings[@"redirectUrl"] length] > 0) {
        settings.paymentsRedirectUrl = redirectSettings[@"redirectUrl"];
    }

    if (jsonSettings[@"webViewType"] && [jsonSettings[@"webViewType"] length] > 0) {
        NSString* webViewType = jsonSettings[@"webViewType"];

        if ([webViewType isEqualToString:@"External"]) {
            settings.openExternalBrowser = YES;
        }
    }
    
    if (jsonSettings[@"webShopUrl"] && [jsonSettings[@"webShopUrl"] length] > 0) {
        NSString* webShopUrl = jsonSettings[@"webShopUrl"];
        settings.webshopUrl = [NSURL URLWithString:webShopUrl];
    }

    if (jsonSettings[@"webViewOrientationLock"] && [jsonSettings[@"webViewOrientationLock"] length] > 0) {
        NSString* orientation = jsonSettings[@"webViewOrientationLock"];
        
        // Unity uses OrientationLock; map to iOS orientation masks.
        if ([orientation isEqualToString:@"Portrait"]) {
            settings.paystationOrientation = UIInterfaceOrientationMaskPortrait;
        } else if ([orientation isEqualToString:@"Landscape"]) {
            settings.paystationOrientation = UIInterfaceOrientationMaskLandscape;
        } else if (![orientation isEqualToString:@"Auto"]) {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported webViewOrientationLock value: %@", orientation);
        }
    }

    if (jsonSettings[@"useBuyButtonSolution"]) {
        settings.useBuyButtonSolution = [jsonSettings[@"useBuyButtonSolution"] boolValue];
    }
    
    if (jsonSettings[@"emailCollectionConsentOptInEnabled"]) {
        settings.collectPartnerEventEmailConsentValue = [jsonSettings[@"emailCollectionConsentOptInEnabled"] numberValue];
    }
    
    if (json[@"simpleMode"] && [json[@"simpleMode"] length] > 0) {
        NSString* simpleMode = json[@"simpleMode"];
        
        if ([simpleMode isEqualToString:@"Off"]) {
            settings.useSimpleMode = NO;
        }
        else if ([simpleMode isEqualToString:@"WebShop"]) {
            settings.useSimpleMode = YES;
            XsollaUnityMobile.shared.useWebShop = YES;
        }
        else if ([simpleMode isEqualToString:@"ServerTokens"]) {
            settings.useSimpleMode = YES;
        } else {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported simpleMode value: %@", simpleMode);
        }
    }

    if (jsonSettings[@"webhooksMode"] && [jsonSettings[@"webhooksMode"] length] > 0) {
        NSString* webhooksMode = jsonSettings[@"webhooksMode"];
        
        if ([webhooksMode isEqualToString:@"EventsApi"]) {
            settings.useEventsApiForPurchaseRestoration = YES;
        } else if ([webhooksMode isEqualToString:@"Webhooks"]) {
            if (settings.useLocalPurchaseRestoration) {
                XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: localPurchasesRestore is enabled while webhooksMode is Webhooks; consider disabling local restoration.");
            }
        } else if (![webhooksMode isEqualToString:@"Off"]) {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported webhooksMode value: %@", webhooksMode);
        }
    }
    
    if (jsonSettings[@"socialProvider"]) {
        NSDictionary* socialProvider = jsonSettings[@"socialProvider"];
        
        if (socialProvider && socialProvider[@"name"] && socialProvider[@"accessToken"] && [socialProvider[@"name"] length] > 0 && [socialProvider[@"accessToken"] length] > 0) {
            XMSocialNetworkProvider provider = XMSocialNetworkProviderXsolla;
            BOOL hasValidProvider = YES;
            NSString* token = socialProvider[@"accessToken"];
            
            if ([socialProvider[@"name"] isEqualToString:@"xsolla"]) {
                provider = XMSocialNetworkProviderXsolla;
            } else if ([socialProvider[@"name"] isEqualToString:@"epicgames"]) {
                provider = XMSocialNetworkProviderEpicgames;
            } else {
                hasValidProvider = NO;
                XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: unsupported socialProvider name value: %@", socialProvider[@"name"]);
            }
            
            if (hasValidProvider) {
                [settings setSocialProviderTokenWithProvider:provider accessToken:token];
            }
        }
    }
    
    if (json[@"locale"] && [json[@"locale"] length] > 0) {
        settings.locale = [NSLocale localeWithLocaleIdentifier:json[@"locale"]];
    }

    if (json[@"userId"] && [json[@"userId"] length] > 0) {
        settings.customUserId = json[@"userId"];
        settings.webshopUserId = json[@"userId"];
    }

    if (json[@"trackingId"] && [json[@"trackingId"] length] > 0) {
        settings.trackingId = json[@"trackingId"];
    }
    
    if (json[@"customPayStationDomainProduction"] && [json[@"customPayStationDomainProduction"] length] > 0) {
        NSURL* productionDomain = [NSURL URLWithString:json[@"customPayStationDomainProduction"]];
        NSURL* sandboxDomain = json[@"customPayStationDomainSandbox"] ? [NSURL URLWithString:json[@"customPayStationDomainSandbox"]] : nil;
        if (productionDomain) {
            [settings setupCustomPaystationDomainWithProductionDomain:productionDomain sandboxDomain:sandboxDomain];
        }
        else {
            XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: invalid production domain - %@", json[@"customPayStationDomainProduction"]);
        }
    }
    else if (json[@"customPayStationDomainSandbox"] && [json[@"customPayStationDomainSandbox"] length] > 0) {
        XsollaUnityLogWithOverride(currentLogLevel, SKLogLevelWarning, @"_XsollaUnityBridgeJsonToConfig settings warning: custom Pay Station domain for sandbox set, but no production domain, this is unsupported.");
    }
    
    // Report any unhandled keys so future additions are visible during integration.
    XsollaLogUnusedKeys(json, [NSSet setWithObjects:
                              @"settings",
                              @"accessToken",
                              @"sandbox",
                              @"logLevel",
                              @"simpleMode",
                              @"locale",
                              @"userId",
                              @"trackingId",
                              @"sdkName",
                              @"sdkVersion",
                              @"fetchProductsWithGeoLocale", // unsupported
                              @"fallbackToDefaultLocaleIfNotSet", // unsupported
                              @"fetchPersonalizedProductsOnly",
                              @"customPayStationDomainProduction",
                              @"customPayStationDomainSandbox",
                              nil], @"root json", currentLogLevel);
    
    XsollaLogUnusedKeys(jsonSettings, [NSSet setWithObjects:
                                       @"projectId",
                                       @"loginId",
                                       @"oauthClientId",
                                       @"localPurchasesRestore",
                                       @"webViewType",
                                       @"webViewOrientationLock",
                                       @"webShopUrl",
                                       @"useBuyButtonSolution",
                                       @"emailCollectionConsentOptInEnabled",
                                       @"webhooksMode",
                                       @"uiSettings",
                                       @"redirectSettings",
                                       @"socialProvider",
                                       @"closeButton", // unsupported
                                       @"webViewSplashScreenImageFilepath", // unsupported
                                       @"advancedSettingsAndroid", // unsupported
                                       @"webViewSplashScreenImageDrawableIdAndroid", // unsupported
                                       nil], @"settings", currentLogLevel);
    
    XsollaLogUnusedKeys(uiSettings, [NSSet setWithObjects:
                                    @"themeSize",
                                    @"themeStyle",
                                    @"customTheme",
                                    @"controlTintColor",
                                    @"barTintColor",
                                    @"visibleLogo",
                                    nil], @"uiSettings", currentLogLevel);
    
    XsollaLogUnusedKeys(redirectSettings, [NSSet setWithObjects:
                                           @"redirectButtonText",
                                           @"redirectDelay",
                                           @"redirectUrl",
                                           nil], @"redirectSettings", currentLogLevel);
    
    settings.enablePayments = YES;
    
    return settings;
}

SKPaymentSettings* _JsonToPaymentSettings(const char* jsonCStr) {
    return _JsonToPaymentSettingsWithLogLevel(jsonCStr, gXsollaLogLevel);
}

NSSet* _JsonToProductRequestSet(const char* jsonCStr) {
    NSString *jsonString = [NSString stringWithUTF8String:jsonCStr];
    NSData *data = [jsonString dataUsingEncoding:NSUTF8StringEncoding];
    NSDictionary* json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    
    return [NSSet setWithArray:json[@"products"]];
}

#pragma mark - Xsolla Unity Bridge

extern "C"
{
    static void XsollaStartProductsRequest(NSString *productIdsJsonString, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        NSSet* products = _JsonToProductRequestSet(productIdsJsonString ? [productIdsJsonString UTF8String] : NULL);
        if (products == nil) {
            XsollaUnityLog(SKLogLevelError, @"_XsollaUnityBridgeProductsRequest failed");
            callback(callbackData, NULL, "Invalid products");
            return;
        }

        SKProductsRequest* request = [[SKProductsRequest alloc] initWithProductIdentifiers:products];
        request.delegate = XsollaUnityMobile.shared;

        NSValue* key = [NSValue valueWithPointer:(const void*)request];

        XsollaUnityMobile.shared.productsCallbacks[key] = ^(NSString *jsonResult, NSString *error) {

            XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeProductsRequest callback %@, %@", jsonResult, error);

            callback(callbackData, jsonResult ? [jsonResult UTF8String] : NULL, error ? [error UTF8String] : NULL);
        };

        [request start];
    }

    void _XsollaUnityBridgeInitialize(const char* settingsJson, const char* additionalSettingsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData, int64_t paymentListener, XsollaUnityBridgeJsonCallbackDelegate paymentEventCallback, int64_t paymentEventCallbackData) {
        SKLogLevel parsedLogLevel = XsollaParseLogLevelFromCString(settingsJson);
        gXsollaLogLevel = parsedLogLevel;
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeInitialize %s", settingsJson);
        
        // set default purchase/restore callback, this one isn't reset
        XsollaUnityMobile.shared.defaultPurchaseCallback = ^(NSString *jsonResult, NSString *error) {
            callback(paymentListener, jsonResult ? [jsonResult UTF8String] : NULL, error ? [error UTF8String] : NULL);
        };
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, parsedLogLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }

        // Parse and apply retry policies from additional settings
        if (additionalSettingsJson != NULL && strlen(additionalSettingsJson) > 0) {
            _ApplyRetryPoliciesToSettings(additionalSettingsJson, settings, parsedLogLevel);
        }

        [[XsollaUnityMobile shared] initialize:settings withCallback:^(NSString *result, NSString *error) {
        
            XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeInitialize callback %@, %@", result, error);
        
            callback(callbackData, result ? [result UTF8String] : NULL, error ? [error UTF8String] : NULL);
        }];
        
        // initialize calls reset, so set stuff after
        
        // store payment event callback
        if (paymentEventCallback != NULL) {
            XsollaUnityMobile.shared.paymentEventCallback = paymentEventCallback;
            XsollaUnityMobile.shared.paymentEventCallbackData = paymentEventCallbackData;
        }
    }

    void _XsollaUnityBridgeRestore() {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeRestore");
                
        XsollaJsonCallbackDelegate callback = XsollaUnityMobile.shared.defaultPurchaseCallback;
        callback(@"{ purchases: [] }", nil);
    }

    void _XsollaUnityBridgeProductsRequest(const char* productIdsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeProductsRequest %s", productIdsJson);
        
        NSString *productIdsJsonString = productIdsJson ? [NSString stringWithUTF8String:productIdsJson] : nil;

        if (XsollaUnityMobile.shared.requiresLoginForPersonalizedProducts && !XsollaUnityMobile.shared.isLoggedInToPaymentQueue) {
            XsollaUnityLog(SKLogLevelVerbose, @"Delaying products request until paymentQueueDidLogin");
            void (^startRequest)(void) = ^{
                XsollaStartProductsRequest(productIdsJsonString, callback, callbackData);
            };
            [XsollaUnityMobile.shared.pendingProductRequests addObject:[startRequest copy]];
            return;
        }

        XsollaStartProductsRequest(productIdsJsonString, callback, callbackData);
    }

    void _XsollaUnityBridgePurchase(const char* skuCStr, const char* developerPayloadCStr /* can be null */, const char* externalId, int paymentMethod, const char* paymentToken, bool allowTokenOnlyFinishedStatusWithoutOrderId, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgePurchase");
        
        if (paymentToken && *paymentToken) {
            NSString* token = [NSString stringWithUTF8String:paymentToken];
            
            XsollaUnityMobile.shared.purchaseDummyCallbacks[token] = ^(NSString *jsonResult, NSString *error) {
                callback(callbackData, jsonResult ? [jsonResult UTF8String] : NULL, error ? [error UTF8String] : NULL);
            };
            
            XMTokenPayment* tokenPayment = [[XMTokenPayment alloc] initWithPaystationToken:token];

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
            
            tokenPayment.allowFinishedWithoutOrderId = (allowTokenOnlyFinishedStatusWithoutOrderId == YES);
            
#pragma clang diagnostic pop
            [[SKPaymentQueue defaultQueue] purchaseWithTokenPayment:tokenPayment];
            return;
        }
        
        NSString* sku = [NSString stringWithUTF8String:skuCStr];
        
        if (XsollaUnityMobile.shared.useWebShop) {
            BOOL res = [[SKPaymentQueue defaultQueue] purchaseWithSKU:sku];
            NSString* err = [[XsollaUnityMobile shared] errorJsonWithMessage:res ? @"Opened Web Shop" : @"Failed to open Web Shop link" code:res ? kXsollaPurchaseErrorCodeCancelled : kXsollaPurchaseErrorCodeInternal];
            callback(callbackData, NULL, [err UTF8String]);
            return;
        }

        SKProduct* product = [XsollaUnityMobile.shared productBySku:sku];
        if (product == nil) {
            NSString* err = [[XsollaUnityMobile shared] errorJsonWithMessage:@"Product not found" code:kXsollaPurchaseErrorCodeInternal];
            callback(callbackData, NULL, [err UTF8String]);
            return;
        }
        
        SKPayment* payment = [SKPayment paymentWithProduct:product];
        NSValue* key = [NSValue valueWithPointer:(const void*)payment];
        
        if (developerPayloadCStr && *developerPayloadCStr) {
            payment.customPayload = [NSString stringWithUTF8String:developerPayloadCStr];
        }

        if (externalId && *externalId) {
            payment.externalIdentifier = [NSString stringWithUTF8String:externalId];
        }
        
        if (paymentMethod >= 0) {
            payment.paymentMethodId = [NSNumber numberWithInt:paymentMethod];
        }
        
        XsollaUnityMobile.shared.purchaseCallbacks[key] = ^(NSString *jsonResult, NSString *error) {
            callback(callbackData, jsonResult ? [jsonResult UTF8String] : NULL, error ? [error UTF8String] : NULL);
        };
        
        [[SKPaymentQueue defaultQueue] addPayment:payment];
    }

    void _XsollaUnityBridgeConsume(const char* skuCStr, int quantity, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeConsume");
        
        NSString* sku = [NSString stringWithUTF8String:skuCStr];
        
        for (SKPaymentTransaction* t in [SKPaymentQueue defaultQueue].transactions) {
            if ([t.payment.productIdentifier isEqualToString:sku]) {
                [[SKPaymentQueue defaultQueue] finishTransaction:t];
                callback(callbackData, "{}", NULL);
                return;
            }
        }
        
        callback(callbackData, NULL, "Transaction not found");
    }

    void _XsollaUnityBridgeValidate(const char* idCStr, const char* skuCStr, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeValidate");
        
        NSString* (^toJson)(BOOL) = ^NSString*(BOOL result) {
            return [NSString stringWithFormat:@"{ success: %@ }", result ? @"true" : @"false"];
        };
        
        if (idCStr == NULL) {
            callback(callbackData, [toJson(NO) UTF8String], "Transaction id is null");
            return;
        }
    
        SKPaymentTransaction* transaction = nil;
        NSString* identifier = [NSString stringWithUTF8String:idCStr];
        NSString* sku = skuCStr ? [NSString stringWithUTF8String:skuCStr] : nil;
        
        for (SKPaymentTransaction* t in [SKPaymentQueue defaultQueue].transactions) {
            BOOL transactionMatch = (t.transactionIdentifier == NULL && t.isRestoredTransaction) || [t.transactionIdentifier isEqualToString:identifier];
            BOOL skuMatch = skuCStr == NULL || t.payment == nil || t.payment.productIdentifier == nil || [t.payment.productIdentifier isEqualToString:sku];
            BOOL somethingPresent = t.transactionIdentifier != NULL || (skuCStr && t.payment && t.payment.productIdentifier); // make sure either id or sku is present
            
            if (transactionMatch && skuMatch && somethingPresent && t.transactionState == SKPaymentTransactionStatePurchased) {
                transaction = t;
                break;
            }
        }
        
        if (transaction) {
            [[SKPaymentQueue defaultQueue] validateTransaction:transaction completion:^(BOOL success, NSError * _Nullable error) {
                XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeValidate callback %d, %@", success, error);

                callback(callbackData, [toJson(success) UTF8String], error ? [error.description UTF8String] : NULL);
            }];
        }
        else {
            callback(callbackData, [toJson(NO) UTF8String], "Transaction not found");
        }
    }

    void _XsollaUnityBridgeLoginWidget(const char* settingsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeLoginWidget %s", settingsJson);
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }
                
        [[XMLoginManager shared] authWithXsollaWidgetWithSettings:settings completion:^(XMAccessTokenInfo * _Nullable token, NSError * _Nullable error) {
            NSString* result = [token toJsonString];
            callback(callbackData, result ? [result UTF8String] : NULL, error ? [error.description UTF8String] : NULL);
        }];
    }

    void _XsollaUnityBridgeLoginWithSocialAccount(const char* settingsJson, const char* providerCStr, const char* accessTokenCStr, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeLoginWithSocialAccount with provider %s and access token %s, %s", providerCStr, accessTokenCStr, settingsJson);
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }
        
        NSString* providerName = providerCStr ? [NSString stringWithUTF8String:providerCStr] : nil;
        NSString* accessToken = accessTokenCStr ? [NSString stringWithUTF8String:accessTokenCStr] : nil;
        
        if (providerName == nil || accessToken == nil) {
            callback(callbackData, NULL, "Invalid provider name / access token");
            return;
        }
        
        XMSocialNetworkProvider provider = XMSocialNetworkProviderXsolla;
        
        if ([providerName isEqualToString:@"xsolla"]) {
            provider = XMSocialNetworkProviderXsolla;
        } else if ([providerName isEqualToString:@"epicgames"]) {
            provider = XMSocialNetworkProviderEpicgames;
        } else {
            XsollaUnityLogWithOverride(logLevel, SKLogLevelWarning, @"_XsollaUnityBridgeLoginWithSocialAccount: unsupported socialProvider name value: %@", providerName);
            callback(callbackData, NULL, "Unsupported social provider");
            return;
        }
        
        [[XMLoginManager shared] authWithSocialProviderWithSettings:settings provider:provider socialAccessToken:accessToken completion:^(XMAccessTokenInfo * _Nullable token, NSError * _Nullable error) {
            NSString* result = [token toJsonString];
            callback(callbackData, result ? [result UTF8String] : NULL, error ? [error.description UTF8String] : NULL);
        }];
    }

    void _XsollaUnityBridgeOpenWebView(const char* settingsJson, const char* urlC, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeLoginWallet %s", settingsJson);
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }
        
#if 0
        NSString* url = [NSString stringWithUTF8String:urlC];
        
        [[XMLoginManager shared] openWebViewWithUrl:[NSURL URLWithString:url] settings:settings completion:^() {
            callback(callbackData, "{}", NULL);
        }];
#else
        callback(callbackData, NULL, "Not implemented");
#endif
    }

    void _XsollaUnityBridgeClearToken(const char* settingsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeClearToken");

        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }

        [[XMLoginManager shared] clearWidgetAuthTokenWithSettings:settings];
        callback(callbackData, "{}", NULL);
    }

    void _XsollaUnityBridgeSetupAnalytics(const char* versionCStr) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeSetupAnalytics");

        [[SKPaymentQueue defaultQueue] setupEngineAnalyticsFor:@"unity" version:[NSString stringWithUTF8String:versionCStr]];
    }

    void _XsollaUnityBridgeDeinitialize(XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeDeinitialize");
        
        [XsollaUnityMobile destroy];
        
        callback(callbackData, "{}", NULL);
    }
    
    void _XsollaUnityBridgeGetAccessToken(XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeGetAccessToken");
        
        NSString* token = [SKPaymentQueue defaultQueue].currentLoginToken;
        NSString* result = [NSString stringWithFormat:@"{ token: \"%@\" }", token];

        if (token)
            callback(callbackData, [result UTF8String], NULL);
        else
            callback(callbackData, NULL, "Not logged in.");
    }

    void _XsollaUnityBridgeUpdateAccessToken(const char* tokenCStr, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeUpdateAccessToken");

        NSString* token = tokenCStr ? [NSString stringWithUTF8String:tokenCStr] : nil;

        if (token == nil || [token length] == 0) {
            callback(callbackData, NULL, "Invalid token, null or empty");
            return;
        }

        SKPaymentSettings* settings = XsollaUnityMobile.shared.settings;
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings, was SDK initialized?");
            return;
        }
        
        settings.customLoginToken = token;
        callback(callbackData, "{}", NULL);
    }

    void _XsollaUnityBridgeRefreshToken(const char* settingsJson, const char* tokenCStr, const char* refreshTokenCStr, int expirationTime, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeRefreshToken");
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }

        NSString* token = [NSString stringWithUTF8String:tokenCStr];
        NSString* refreshToken = [NSString stringWithUTF8String:refreshTokenCStr];
        NSDate* expiresIn = [NSDate dateWithTimeIntervalSinceNow:expirationTime];

        XMAccessTokenInfo* tokenInfo = [[XMAccessTokenInfo alloc] initWithAccessToken:token expiresIn:expiresIn refreshToken:refreshToken tokenType:@"bearer"];

        [[XMLoginManager shared] refreshWidgetAuthToken:tokenInfo settings:settings completion:^(XMAccessTokenInfo * _Nullable token, NSError * _Nullable error) {
            NSString* result = [token toJsonString];
            callback(callbackData, result ? [result UTF8String] : NULL, result ? NULL : (error ? [error.description UTF8String] : "failed to parse token"));
        }];
    }

    void _XsollaUnityBridgeGetWebViewDismissUrl(const char* settingsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeGetWebViewDismissUrl");
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }

        NSString* result = [NSString stringWithFormat:@"{ url: \"%@\" }", settings.webViewDismissRedirectUrl];

        callback(callbackData, [result UTF8String], NULL);
    }

    void _XsollaUnityBridgeLoadWidgetAuthToken(const char* settingsJson, XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        SKLogLevel logLevel = XsollaParseLogLevelFromCString(settingsJson);
        XsollaUnityLogWithOverride(logLevel, SKLogLevelVerbose, @"_XsollaUnityBridgeLoadWidgetAuthToken");
        
        SKPaymentSettings* settings = _JsonToPaymentSettingsWithLogLevel(settingsJson, logLevel);
        if (settings == nil) {
            callback(callbackData, NULL, "Invalid settings");
            return;
        }

        [[XMLoginManager shared] loadWidgetAuthTokenWithSettings:settings completion:^(XMAccessTokenInfo * _Nullable token, NSError * _Nullable error) {
            NSString* result = [token toJsonString];
            callback(callbackData, result ? [result UTF8String] : NULL, result ? NULL : (error ? [error.description UTF8String] : "failed to parse token"));
        }];
    }
    
    void _XsollaUnityBridgeGetAppleStorefront(XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeGetAppleStorefront");
        
        [SKPaymentQueue loadCurrentStorefrontCountryCodeWithCompletion:^(NSString * _Nullable countryCode) {
            NSString* result = [NSString stringWithFormat:@"{ storefront: \"%@\" }", countryCode ? countryCode : @""];
            
            callback(callbackData, [result UTF8String], NULL);
        }];
    }

    void _XsollaUnityBridgeGetAppleDistribution(XsollaUnityBridgeJsonCallbackDelegate callback, int64_t callbackData) {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeGetAppleDistribution");
        
        [SKPaymentQueue checkIfRunningInAlternativeDistributionWithCompletion:^(BOOL isRunningInAlternativeDistribution) {
            NSString* result = [NSString stringWithFormat:@"{ isRunningInAlternativeDistribution: %@ }", isRunningInAlternativeDistribution ? @"true" : @"false"];
            
            callback(callbackData, [result UTF8String], NULL);
        }];
    }

    void _XsollaUnityBridgeCancelTransaction() {
        XsollaUnityLog(SKLogLevelVerbose, @"_XsollaUnityBridgeCancelTransaction");
        
        BOOL wasCancelled = [[SKPaymentQueue defaultQueue] cancelTransaction];
    }
}
