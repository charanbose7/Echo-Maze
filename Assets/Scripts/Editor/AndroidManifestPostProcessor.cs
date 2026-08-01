#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Injects android.permission.VIBRATE into the generated manifest.
///
/// This is not optional. Unity decides which permissions to add by scanning the built assemblies
/// for known API calls (Handheld.Vibrate, Microphone, etc.). Haptics reaches the vibrator through
/// AndroidJavaObject/JNI, which that scanner cannot see, so without this the APK ships with no
/// VIBRATE permission and every vibrate() call is dropped by the framework — silently, with no
/// exception and nothing in logcat.
///
/// Verify on a built APK with:  aapt dump permissions &lt;apk&gt;
/// </summary>
public class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
{
    private const string Permission = "android.permission.VIBRATE";

    // After Unity's own manifest generation, so the file exists and nothing overwrites us.
    public int callbackOrder => 1;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogError("[EchoMaze] AndroidManifest.xml not found at " + manifestPath +
                           " — VIBRATE permission NOT added, haptics will be dead in this build.");
            return;
        }

        var doc = new XmlDocument();
        doc.Load(manifestPath);

        var manifest = doc.SelectSingleNode("/manifest") as XmlElement;
        if (manifest == null) { Debug.LogError("[EchoMaze] Malformed AndroidManifest.xml."); return; }

        const string ns = "http://schemas.android.com/apk/res/android";

        foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
        {
            var el = node as XmlElement;
            if (el != null && el.GetAttribute("name", ns) == Permission)
            {
                Debug.Log("[EchoMaze] VIBRATE permission already present.");
                return;
            }
        }

        var added = doc.CreateElement("uses-permission");
        added.SetAttribute("name", ns, Permission);
        manifest.AppendChild(added);
        doc.Save(manifestPath);

        Debug.Log("[EchoMaze] Added " + Permission + " to " + manifestPath);
    }
}
#endif
