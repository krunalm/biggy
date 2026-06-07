namespace Inflector {
  // Minimal stand-in for the net45-only `Inflector` package so BiggyList.cs can
  // be compiled into the net8.0 characterization/benchmark harness. BiggyList
  // only calls Inflector.Inflector.Pluralize when no dbName is supplied; every
  // test/benchmark here passes an explicit dbName, so this method is never hit
  // at runtime and its exact pluralization is irrelevant to what we measure.
  public static class Inflector {
    public static string Pluralize(string word) {
      if (string.IsNullOrEmpty(word)) return word;
      return word.EndsWith("s") ? word : word + "s";
    }
  }
}
