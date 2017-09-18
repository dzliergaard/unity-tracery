using System.Collections;
using NUnit.Framework;

namespace UnityTracery.Tests {
  public class GrammarTestItem {
    public static IEnumerable TestCases {
      get {
        yield return new TestCaseData("a").Returns("a");
        yield return new TestCaseData("Emma Woodhouse, handsome, clever, and rich, with a comfortable home and happy disposition, seemed to unite some of the best blessings of existence; and had lived nearly twenty-one years in the world with very little to distress or vex her.")
            .Returns("Emma Woodhouse, handsome, clever, and rich, with a comfortable home and happy disposition, seemed to unite some of the best blessings of existence; and had lived nearly twenty-one years in the world with very little to distress or vex her.");
        yield return new TestCaseData(@"\#escape hash\# and escape slash\").Returns(@"\#escape hash\# and escape slash\");
        yield return new TestCaseData("#deepHash# [myColor:#deeperHash#] #myColor#").Returns(@"\#FF00FF  \#FF00FF");
        yield return new TestCaseData("\"test\" and 'test'").Returns("\"test\" and 'test'");
        yield return new TestCaseData(@"\[nonaction\]").Returns(@"\[nonaction\]");
        yield return new TestCaseData(@"\#nonhashed\#").Returns(@"\#nonhashed\#");
        yield return new TestCaseData(@"\\").Returns(@"\\");
        yield return new TestCaseData(@"\\[escaped\\:escaped\\]\\#escaped\\#").Returns(@"\\escaped\\");
        yield return new TestCaseData("Action with options: [action:one,two,three] #action# #action# #action#").Returns("Action with options: ");
        yield return new TestCaseData("Action with nested and overwriting symbols: [action:#one#] #action# #[action:#two#]two# #action#").Returns("Action with nested and overwriting symbols: one two one");
        yield return new TestCaseData("Action overwrite and restore: [action:action]#action# #[action:inner-action]action# #action#").Returns("Action overwrite and restore: action inner-action action");
        yield return new TestCaseData("Rule with action & pop: #one# [one:one2]#one# [one:POP]#one#").Returns("Rule with action & pop: one one2 one");
        yield return new TestCaseData("Rule with inner-action & out-of-scope pop: #one# #[one:one2]one# #one#").Returns("Rule with inner-action & out-of-scope pop: one one2 one");
        yield return new TestCaseData("An action can have inner tags: [key:#deepHash#] #key#").Returns(@"An action can have inner tags: \#00FF00");
        yield return new TestCaseData(@"&\#x2665; &\#x2614; &\#9749; &\#x2665;").Returns("a");
        yield return new TestCaseData("<svg width='100' height='70'><rect x='0' y='0' width='100' height='100' #svgStyle#/> <rect x='20' y='10' width='40' height='30' #svgStyle#/></svg>").Returns("a");
        yield return new TestCaseData("[pet:#animal#]You have a #pet#. Your #pet# is #mood#.").Returns("a");
        yield return new TestCaseData("[pet:#animal#]You have a #pet#. [pet:#animal#]Pet:#pet# [pet:POP]Pet:#pet#").Returns("a");
        yield return new TestCaseData("#[pet:#animal#]nonrecursiveStory# post:#pet#").Returns("a");
        yield return new TestCaseData("#origin#").Returns("a");
        yield return new TestCaseData("#animal.foo#").Returns("a");
        yield return new TestCaseData("[pet:#animal#]#nonrecursiveStory# -> #nonrecursiveStory.replace(beach,mall)#").Returns("a");
        yield return new TestCaseData("[pet:#animal#]#recursiveStory#").Returns("a");
        yield return new TestCaseData("#unmatched").Returns("a");
        yield return new TestCaseData("#unicorns#").Returns("a");
        yield return new TestCaseData("[pet:unicorn").Returns("a");
        yield return new TestCaseData("pet:unicorn]").Returns("a");
        yield return new TestCaseData("[][]][][][[[]]][[]]]]").Returns("a");
        yield return new TestCaseData("[][#]][][##][[[##]]][#[]]]]").Returns("a");
      }
    }
  }
}
