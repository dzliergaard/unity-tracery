using System.IO;
using NUnit.Framework;

namespace UnityTracery.Tests {
  [TestFixture]
  public class GrammarTests {
    private TraceryGrammar grammar;

    [OneTimeSetUp]
    public void Setup() {
      StreamReader reader = new StreamReader(new FileStream("Tests/test-grammar.json", FileMode.Open));
      var grammarString = reader.ReadToEnd();
      grammar = new TraceryGrammar(grammarString);
      JSONObjectExtensions.SeedRandom(0);
    }

    [Test, TestCaseSource(typeof(GrammarTestItem), "TestCases")]
    public string ResolveTests(string input) {
      return grammar.Resolve(input);
    }
  }
}
