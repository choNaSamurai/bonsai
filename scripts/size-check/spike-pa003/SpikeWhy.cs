using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class SpikeWhy
{
    // Dumps engine-module inclusion reasons from the last build report.
    public static void Why()
    {
        var r = BuildReport.GetLatestReport();
        var sb = new StringBuilder();
        if (r == null || r.strippingInfo == null) { sb.AppendLine("no report"); }
        else
        {
            foreach (var m in r.strippingInfo.includedModules)
            {
                sb.Append(m).Append(" <= ");
                foreach (var why in r.strippingInfo.GetReasonsForIncluding(m)) sb.Append(why).Append(" | ");
                sb.AppendLine();
            }
        }
        File.WriteAllText("../out/why_" + Path.GetFileName(Directory.GetCurrentDirectory()) + ".txt", sb.ToString());
    }
}
