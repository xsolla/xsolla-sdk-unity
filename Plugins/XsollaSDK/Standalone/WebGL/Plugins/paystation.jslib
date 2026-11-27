mergeInto(LibraryManager.library, {

	OpenPayStationWidget__deps: ['ClosePayStationWidget'],

	OpenPayStationWidget: function (token, sandbox, sdkType, engineVersion, sdkVersion, applePayMerchantDomain, appearanceJson, externalBrowser, externalUrl) {
		var jsToken = UTF8ToString(token);
		var isSandbox = sandbox > 0 ? true : false;
        var appearanceTxt = UTF8ToString(appearanceJson);
		var appearance = JSON.parse(appearanceTxt);
		var options = {
			access_token: jsToken,
			sandbox: isSandbox,
			lightbox: {
				width: '740px',
				height: '760px',
				spinner: 'round',
				spinnerColor: '#cccccc'
			},
			queryParams: {
                sdk: UTF8ToString(sdkType),
                sdk_v: UTF8ToString(sdkVersion),
                engine: 'unity',
                engine_v: UTF8ToString(engineVersion),
                browser_type: 'iframe',
                build_platform: 'webglplayer'
            }
		};
		
		if (appearance.widgetWidth != null && appearance.widgetWidth !== "") {
            options.lightbox.width = appearance.widgetWidth;
        }
        
        if (appearance.widgetHeight != null && appearance.widgetHeight !== "") {
            options.lightbox.height = appearance.widgetHeight;
        }
        
        if (appearance.zIndex != null) {
            options.lightbox.zIndex = appearance.zIndex;
        }
        
        if (appearance.overlayOpacity != null) {
            options.lightbox.overlayOpacity = appearance.overlayOpacity;
        }
        
        if (appearance.overlayBackgroundColor != null && appearance.overlayBackgroundColor !== "") {
            options.lightbox.overlayBackground = appearance.overlayBackgroundColor;
        }
        
        if (appearance.isModal != null) {
            options.lightbox.modal = appearance.isModal;
        }
        
        if (appearance.closeByClickOverlay != null) {
            options.lightbox.closeByClick = appearance.closeByClickOverlay;
        }
        
        if (appearance.closeByKeyboardEscape != null) {
            options.lightbox.closeByKeyboard = appearance.closeByKeyboardEscape;
        }
        
        if (appearance.contentBackgroundColor != null && appearance.contentBackgroundColor !== "") {
            options.lightbox.contentBackground = appearance.contentBackgroundColor;
        }
        
        if (appearance.contentMargin != null && appearance.contentMargin !== "") {
            options.lightbox.contentMargin = appearance.contentMargin;
        }
        
        if (appearance.spinnerType != null && appearance.spinnerType !== "") {
            options.lightbox.spinner = appearance.spinnerType;
        }
        
        if (appearance.spinnerColor != null && appearance.spinnerColor !== "") {
            options.lightbox.spinnerColor = appearance.spinnerColor;
        }
        
        if (appearance.spinnerUrl != null && appearance.spinnerUrl !== "") {
            options.lightbox.spinnerUrl = appearance.spinnerUrl;
        }
        
        if (appearance.spinnerRotationPeriod != null) {
            options.lightbox.spinnerRotationPeriod = appearance.spinnerRotationPeriod;
        }
        
		var jsParentDomain = UTF8ToString(applePayMerchantDomain);
		if (jsParentDomain !== "") {
            options.queryParams.parent_domain = jsParentDomain;
        }

		var openWidget = function () {
			// Listeners an earlier open left, if it was never closed through ClosePayStationWidget, would answer
			// this purchase twice.
			XPayStationWidget.off();

			XPayStationWidget.on(XPayStationWidget.eventTypes.STATUS, function (event, data) {
				Module.SendMessage('XsollaWebCallbacks', 'PublishPaymentStatusUpdate');

                let status = null;
                if (data && data.paymentInfo && data.paymentInfo.status) {
                    status = data.paymentInfo.status;
                }

                if (status === 'done') {
                    _ClosePayStationWidget();

                    Module.SendMessage('XsollaWebCallbacks', 'PublishPaymentClose');
                }

            });

            XPayStationWidget.on(XPayStationWidget.eventTypes.CLOSE, function (event, data) {
				if (data === undefined) {
					Module.SendMessage('XsollaWebCallbacks', 'PublishPaymentCancel');
				}
				else {
					Module.SendMessage('XsollaWebCallbacks', 'PublishPaymentStatusUpdate');
					Module.SendMessage('XsollaWebCallbacks', 'PublishPaymentClose');
				}
			});

			XPayStationWidget.init(options);
			XPayStationWidget.open();
		};

		// The widget script is loaded once per page. While it loads, only the latest purchase waits to open, as the
		// SDK answers only the latest one.
		var scriptId = 'xsolla-paystation-widget-script';
		var script = document.getElementById(scriptId);

		if (typeof XPayStationWidget !== 'undefined') {
			openWidget();
		}
		else if (script) {
			script.xsollaOpenWidget = openWidget;
		}
		else {
			script = document.createElement('script');
			script.id = scriptId;
			script.type = "text/javascript";
			script.async = true;
			script.src = "https://cdn.xsolla.net/payments-bucket-prod/embed/1.5.0/widget.min.js";
			script.xsollaOpenWidget = openWidget;

			script.addEventListener('load', function () {
				script.xsollaOpenWidget();
			}, false);

			// Without the script nothing opens, so the next purchase loads it again.
			script.addEventListener('error', function () {
				if (script.parentNode) {
					script.parentNode.removeChild(script);
				}
			}, false);

			document.getElementsByTagName('head')[0].appendChild(script);
		}
	},

	ClosePayStationWidget: function () {
		if (typeof XPayStationWidget !== 'undefined') {
			XPayStationWidget.off();
		}

		// A close by the user runs this from the widget's own close event, before the widget removes its lightbox:
		// removing it here first made that removal throw and left the page unscrollable. By a later tick that close
		// has removed the lightbox. One still attached means the SDK closes the widget (after a payment, whichever
		// of the status listener or the order check sees it first), so it closes through the widget: only the
		// widget's own close listener restores the page's scrolling and unsubscribes itself, and with the SDK's
		// listeners already off it reports no cancel. A tick after that removes what a modal widget, which ignores
		// close, leaves.
		var removeLeftovers = function () {
			var elements = Array.prototype.slice.call(document.getElementsByClassName('xpaystation-widget-lightbox'));
			for (var i = 0; i < elements.length; i++) {
				if (elements[i].parentNode) {
					elements[i].parentNode.removeChild(elements[i]);
				}
			}
		};

		setTimeout(function () {
			if (document.getElementsByClassName('xpaystation-widget-lightbox').length > 0 && typeof XPayStationWidget !== 'undefined') {
				XPayStationWidget.close();
				setTimeout(removeLeftovers, 0);
			}
		}, 0);
	},
	
	ShowPopupAndOpenPayStation: function (url, popupMessage, continueButtonText, cancelButtonText) {
        var jsUrl = UTF8ToString(url);
        var jsPopupMessage = UTF8ToString(popupMessage);
        var jsContinueButtonText = UTF8ToString(continueButtonText);
        var jsCancelButtonText = UTF8ToString(cancelButtonText);

        var overlay = document.createElement('div');
        overlay.style.position = 'fixed';
        overlay.style.top = '0';
        overlay.style.left = '0';
        overlay.style.width = '100%';
        overlay.style.height = '100%';
        overlay.style.backgroundColor = 'rgba(0, 0, 0, 0.5)';
        overlay.style.zIndex = '1000';
        overlay.style.display = 'flex';
        overlay.style.alignItems = 'center';
        overlay.style.justifyContent = 'center';

        var popup = document.createElement('div');
        popup.style.backgroundColor = '#ffffff';
        popup.style.padding = '20px';
        popup.style.borderRadius = '10px';
        popup.style.boxShadow = '0 4px 8px rgba(0, 0, 0, 0.2)';
        popup.style.textAlign = 'center';

        var messageElement = document.createElement('p');
        messageElement.textContent = jsPopupMessage;
        messageElement.style.margin = '20px';
        messageElement.style.fontSize = '16px';
        popup.appendChild(messageElement);

        var cancelButton = document.createElement('button');
        cancelButton.textContent = jsCancelButtonText;
        cancelButton.style.padding = '10px 20px';
        cancelButton.style.margin = '0 5px';
        cancelButton.style.border = 'none';
        cancelButton.style.borderRadius = '5px';
        cancelButton.style.borderWidth = '1px';
        cancelButton.style.borderColor = '#6939F9';
        cancelButton.style.borderStyle = 'solid';
        cancelButton.style.backgroundColor = '#ffffff';
        cancelButton.style.color = '#6939F9';
        cancelButton.style.cursor = 'pointer';
        
        cancelButton.addEventListener('click', function () {
            document.body.removeChild(overlay);
        });
        
        var continueButton = document.createElement('button');
        continueButton.textContent = jsContinueButtonText;
        continueButton.style.padding = '10px 20px';
        continueButton.style.margin = '0 5px';
        continueButton.style.border = 'none';
        continueButton.style.borderRadius = '5px';
        continueButton.style.borderWidth = '1px';
        continueButton.style.borderColor = '#6939F9';
        continueButton.style.borderStyle = 'solid';
        continueButton.style.backgroundColor = '#6939F9';
        continueButton.style.color = '#ffffff';
        continueButton.style.cursor = 'pointer';
        
        continueButton.addEventListener('click', function () {
            document.body.removeChild(overlay);
            window.open(jsUrl, '_blank');
        });

        popup.appendChild(cancelButton);
        popup.appendChild(continueButton);
        overlay.appendChild(popup);
        document.body.appendChild(overlay);
    },
    
    GetUserAgent: function () {
        var str = navigator.userAgent;
        var lengthBytes = lengthBytesUTF8(str) + 1;
        var stringOnWasmHeap = _malloc(lengthBytes);
        stringToUTF8(str, stringOnWasmHeap, lengthBytes);
        return stringOnWasmHeap;
    },
    
    GetBrowserLanguage: function () {
        var language = navigator.language || navigator.userLanguage;
        var length = lengthBytesUTF8(language) + 1;
        var buffer = _malloc(length);
        stringToUTF8(language, buffer, length);
        return buffer;
    },
});
