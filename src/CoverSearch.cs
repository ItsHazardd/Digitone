using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Digitone
{
    internal static class CoverSearch
    {
        internal static string Address(string artist)
        {
            return "https://covers.musichoarders.xyz/?remote.port=browser&remote.agent=Digitone&remote.text=Click%20a%20cover%20to%20send%20it%20to%20Digitone&sources=itunes,spotify,tidal&country=us&artist=" + Uri.EscapeDataString(artist ?? "");
        }
        internal static bool TrustedPage(string address) { Uri uri; return Uri.TryCreate(address,UriKind.Absolute,out uri) && uri.Scheme == "https" && uri.Host == "covers.musichoarders.xyz" && uri.IsDefaultPort; }
        internal static string Picture(string message)
        {
            if (String.IsNullOrEmpty(message) || message.Length > 65536) return null;
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(message); object type, picture;
            if (data == null || !data.TryGetValue("type",out type) || Convert.ToString(type) != "pick" || !data.TryGetValue("smallCoverUrl",out picture)) return null;
            Uri uri; string address = Convert.ToString(picture);
            return Uri.TryCreate(address,UriKind.Absolute,out uri) && ArtworkFinder.Allowed(uri) ? address : null;
        }
    }
}
