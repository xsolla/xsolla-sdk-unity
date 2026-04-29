using System;

namespace Xsolla.Core
{
    /// <summary>
    /// Marker for POST requests that must send `{}` as the body. Without an
    /// instance, the no-body PostRequest overload omits the upload handler
    /// entirely, which some endpoints reject for missing Content-Type or
    /// for an empty content length.
    /// </summary>
    [Serializable]
    internal class EmptyBody { }
}
