using System;
using System.IO;
using System.Text.Json;
namespace SrcCad.App;
internal static class WindowsRegressionJournal
{
    public static bool Active => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SRCCAD_UI_REGRESSION_REPORT"));
    public static void Write(object state)
    {
        var path=Environment.GetEnvironmentVariable("SRCCAD_UI_REGRESSION_REPORT");
        if(string.IsNullOrWhiteSpace(path))return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
    }
    public static void Fatal(Exception exception)
        => Write(new {Completed=true,Passed=0,Failed=1,Step="Unhandled UI exception",Failure=exception.ToString()});
}
