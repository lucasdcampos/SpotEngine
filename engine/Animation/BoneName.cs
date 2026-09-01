using System.Text.RegularExpressions;

namespace Spot.Animation;

/// <summary>
/// Canonicalizes bone/node names so animation channels and skinning bones retarget across Mixamo exports.
/// </summary>
/// <remarks>
/// Mixamo tags every skeleton with a namespace whose number varies per download ("mixamorig:", "mixamorig5:",
/// ...), so a clip authored against one rig targets "mixamorig5:Hips" while the model in the scene is
/// "mixamorig:Hips". Canonicalizing that namespace lets a clip retarget onto the same skeleton regardless of
/// the number. Names without the namespace pass through unchanged.
/// </remarks>
public static class BoneName
{
    private static readonly Regex s_mixamoNamespace = new(@"mixamorig\d+:", RegexOptions.Compiled);

    /// <summary>Returns <paramref name="name"/> with its Mixamo skeleton namespace canonicalized to <c>mixamorig:</c>.</summary>
    public static string Normalize(string name) => s_mixamoNamespace.Replace(name, "mixamorig:");
}
