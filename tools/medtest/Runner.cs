using System; using System.Linq; using System.Reflection;
public static class Runner {
  public static int Main(string[] args) {
    int pass=0, fail=0;
    foreach (var t in typeof(Runner).Assembly.GetTypes().Where(t => t.Namespace!=null && t.Namespace.Contains("Test"))) {
      foreach (var m in t.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name=="TestAttribute"))) {
        try { var o = Activator.CreateInstance(t); m.Invoke(o, null); pass++; }
        catch (TargetInvocationException e) { fail++; Console.WriteLine("FAIL " + t.Name + "." + m.Name + ": " + e.InnerException.Message.Trim()); }
      }
    }
    Console.WriteLine($"passed={pass} failed={fail}");
    return fail;
  }
}
