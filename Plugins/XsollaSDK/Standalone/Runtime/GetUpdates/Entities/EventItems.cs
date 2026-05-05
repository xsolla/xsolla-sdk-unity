using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using Xsolla.Core;

namespace Xsolla.GetUpdates
{
    [Serializable]
    internal class EventItemDataSettings
    {
        //public int merchant_id;
        public string project_id;
    }
    
    [Serializable]
    internal class EventItemDataUser
    {
        [CanBeNull] public string id;
        [CanBeNull] public string external_id;
    }
    
    [Serializable]
    internal class EventItemDataPurchaseOrderItemPrice
    {
        public float amount;
        public string currency;
    }
    
    [Serializable]
    internal class EventItemDataPurchaseOrderItem
    {
        public int id;
        public string sku;
        public int quantity;

        public EventItemDataPurchaseOrderItemPrice price;
        public EventItemDataPurchaseOrderItemPrice price_before_discount;
    }
    
    [Serializable]
    internal class EventItemDataItemCAPI
    {
        public string sku;
        public int quantity;
        public string type;
        public string amount;
    }
    
    [Serializable]
    internal class EventItemDataOrderCAPI
    {
        public int id;
        public string currency;
        public string currency_type;
        public string invoice_id;
        public string amount;
        public string status;
        public string mode;
    }
    
    [Serializable]
    internal class EventItemDataPurchaseOrder
    {
        public int id;
        public EventItemDataPurchaseOrderItem[] lineitems;
    }
    
    [Serializable]
    internal class EventItemDataPurchase
    {
        public string id;
        [CanBeNull] public EventItemDataPurchaseOrder order;
    }
    
    [Serializable]
    internal class EventItemDataPurchaseCAPI
    {
        public EventItemDataPurchaseOrderItemPrice total;
    }
    
    [Serializable]
    internal class EventItemDataTransaction
    {
        public string id;
    }
    
    [Serializable]
    internal class EventItemDataBillingCAPI
    {
        public string notification_type;
        [CanBeNull] public EventItemDataSettings settings;
        [CanBeNull] public EventItemDataPurchaseCAPI purchase;
        [CanBeNull] public EventItemDataTransaction transaction;
    }
    
    [Serializable]
    internal class EventItemData
    {
        public string notification_type;
        [CanBeNull] public EventItemDataBillingCAPI billing; // capi
        [CanBeNull] public EventItemDataSettings settings;
        [CanBeNull] public EventItemDataUser user;
        [CanBeNull] public EventItemDataPurchase purchase;
        [CanBeNull] public EventItemDataTransaction transaction;
        [CanBeNull] public Dictionary<string, string> custom_parameters;

        [CanBeNull] public EventItemDataOrderCAPI order;
        [CanBeNull] public EventItemDataItemCAPI[] items;
    }
    
    [Serializable]
    internal class EventItem
    {
        public int id;
        public int status;
        public string created_at;
        public EventItemData data;
    }
    
    [Serializable]
    internal class EventItems
    {
        public EventItem[] events;
    }
    
    internal class EventCache
    {
        public int id;
        public int subid;
        public string transaction_id;
        public string sku;
    }
    
    internal class EventOrderCache
    {
        public int id;
        public string transaction_id;
        public string sku;
    }

    internal class PaymentEvent
    {
        public int id;
        public string created_at;
        
        public int order_id;
        public string order_status;
        public string transaction_id;
        public string sku;
        public int quantity;
        
        public int priceAmount;
        public int priceAmountBeforeDiscount;
        public string priceCurrency;
        
        public Dictionary<string, string> custom_parameters;

        public bool isFree; 

        public PaymentEvent(EventItem evt)
        {
            id = evt.id;
            created_at = evt.created_at;
            
            isFree = IsFreeItem(evt);
            
            if (evt.data.notification_type == "payment")
            {
                order_id = evt.data.purchase?.order?.id ?? 0;
                order_status = isFree ? "free" : "paid";
                transaction_id = isFree ? (evt.data.transaction?.id ?? Guid.NewGuid().ToString()) : evt.data.transaction?.id;
                if (evt.data.purchase?.order?.lineitems != null && evt.data.purchase.order.lineitems.Length > 0)
                {
                    sku = evt.data.purchase.order.lineitems[0].sku;
                    quantity = evt.data.purchase.order.lineitems[0].quantity;

                    priceAmount = (int)Math.Round(evt.data.purchase.order.lineitems[0].price.amount * 100.0f);
                    priceAmountBeforeDiscount = (int)Math.Round(evt.data.purchase.order.lineitems[0].price_before_discount.amount * 100.0f);
                    priceCurrency = evt.data.purchase.order.lineitems[0].price.currency;
                }
                else
                {
                    sku = string.Empty;
                    quantity = 0;
                    priceAmount = 0;
                    priceAmountBeforeDiscount = 0;
                    priceCurrency = "USD";
                }
            }
            else if (evt.data.notification_type == "order_paid")
            {
                order_id = evt.data.order?.id ?? 0;
                order_status = isFree ? "free" : (evt.data.order?.status ?? "unknown");
                transaction_id = isFree ? (evt.data.order?.invoice_id ?? Guid.NewGuid().ToString()) : evt.data.order?.invoice_id;
                var firstItem = evt.data.items?.Length > 0 ? evt.data.items[0] : null;
                sku = firstItem?.sku ?? string.Empty;
                quantity = firstItem?.quantity ?? 0;

                priceAmount = ResolvePriceCents(evt.data);
                priceAmountBeforeDiscount = priceAmount;
                priceCurrency = evt.data.billing?.purchase?.total.currency ?? evt.data.order?.currency;
            }
            else
            {
                XDebug.LogError($"Event {evt.id} has unknown notification type: {evt.data.notification_type}. Skipping.");
                return;
            }

            if (evt.data.custom_parameters != null)
                custom_parameters = new Dictionary<string, string>(evt.data.custom_parameters);
            else
                custom_parameters = new Dictionary<string, string>();
        }

        private static int ResolvePriceCents(EventItemData data)
        {
            if (data.billing?.purchase != null)
                return (int)Math.Round(data.billing.purchase.total.amount * 100.0f);

            if (data.order?.amount != null && float.TryParse(data.order.amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                return (int)Math.Round(amount * 100.0f);

            return 0;
        }

        public static bool IsFreeItem(EventItem evt)
        {
            return evt.data.notification_type == "order_paid" 
                   && evt.data.items != null && evt.data.items.Length > 0 
                   && evt.data.items[0].amount == "0"
                   && evt.data.order != null && evt.data.order.currency == null && evt.data.order.currency_type == "unknown";
        }
        
        public OrderStatus ToOrderStatus()
        {
            return new OrderStatus
            {
                order_id = order_id,
                transaction_id = transaction_id,
                status = isFree ? "free" : "paid",
                receipt = ParseUtils.ToJson(this),
                
                content = new OrderContent
                {
                    price = new Price
                    {
                        amount = $"{priceAmount}", 
                        amount_without_discount = $"{priceAmountBeforeDiscount}",
                        currency = priceCurrency
                    },
                    items = new[]
                    {
                        new OrderItem
                        {
                            sku = sku,
                            quantity = quantity,
                            price = new Price
                            {
                                amount = $"{priceAmount}", 
                                amount_without_discount = $"{priceAmountBeforeDiscount}",
                                currency = priceCurrency
                            },
                        }
                    }
                }
            };
        }

        public EventCache toEventCache(int subid = -1)
        {
            return new EventCache
            {
                id = id,
                subid = subid,
                transaction_id = transaction_id,
                sku = sku
            };
        }
    }
}