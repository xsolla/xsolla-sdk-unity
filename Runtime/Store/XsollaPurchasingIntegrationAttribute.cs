using System;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Marks an assembly that provides the Unity IAP integration in place of <c>XsollaPurchasingModule</c>.
    /// </summary>
    /// <remarks>
    /// While an assembly carrying this attribute is loaded in the editor, the SDK does not warn that the installed
    /// Unity IAP version disables <c>XsollaPurchasingModule</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class XsollaPurchasingIntegrationAttribute : Attribute
    {
    }
}
