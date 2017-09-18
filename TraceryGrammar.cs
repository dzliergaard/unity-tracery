using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace UnityTracery {
  /// <summary>
  /// A Grammar object as described in @GalaxyKate's tracery.io.
  /// </summary>
  [Serializable]
  public class TraceryGrammar {

    private static readonly Regex open_tag_regex = new Regex(EnforceNonEscaped(@"(<tag>[\[#])"));
    /// <summary>
    /// Captures [bracketed] actions in text, which can have inner [actions] as
    /// well but must be bracket-balanced.
    /// </summary>
    private static readonly Regex action_regex = CreateBalancedRegex(@"\[", @"\]");

    /// <summary>
    /// Matches #tags#, which can have inner [actions] with their own #tags#, but
    /// must be bracket-balanced.
    /// </summary>
    private static readonly Regex tag_regex = CreateBalancedRegex(@"\#", @"\#");

    /// <summary>
    /// Matches non-escaped '.' characters within a tag to separate symbol from modifiers.
    /// </summary>
    private static readonly Regex modifier_regex = new Regex(EnforceNonEscaped(@"\.")));

    /// <summary>
    /// Separates [save:option 1, #deephashoption#] actions into key `save` and capture Group `options`.
    /// </summary>
    private static readonly Regex action_components_regex = new Regex(string.Format(@"
        (?<key>(?:[^:]|{{0}})*)
        (?<postkey>{{1}}(?:(?<options>(?:{{2}}|[^,])*)*){{3}}?)?$",
        EnforceEscaped(":"),
        EnforceNonEscaped(":"),
        EnforceEscaped(","),
        EnforceNonEscaped(",")), RegexOptions.IgnorePatternWhitespace);

    public string rawGrammar;

    /// <summary>
    /// Key/value store for grammar rules.
    /// </summary>
    public JSONObject grammar;

    /// <summary>
    /// Key/value store for savable data.
    /// </summary>
    private JSONObject saveData;

    /// <summary>
    /// Modifier function table.
    /// </summary>
    public Dictionary<string, Func<string, string>> ModifierLookup;

    public TraceryGrammar(string source) {
      rawGrammar = (source ?? "").Trim();
      if (!ParseRulesJson()) {
        throw new Exception("Input grammar is not valid JSON.");
      }

      // Set up the standard modifiers.
      ModifierLookup = new Dictionary<string, Func<string, string>> {
        {"a", Modifiers.Article},
        {"beeSpeak", Modifiers.BeeSpeak},
        {"capitalize", Modifiers.Capitalize},
        {"capitalizeAll", Modifiers.CapitalizeAll},
        {"comma", Modifiers.Comma},
        {"ed", Modifiers.PastTense},
        {"inQuotes", Modifiers.InQuotes},
        {"s", Modifiers.Pluralize},
        {"titleCase", Modifiers.TitleCase}
      };

      // Initialize saveData map used for actions.
      saveData = new JSONObject();
    }

    /// <summary>
    /// Resolves the default starting symbol "#origin#".
    /// </summary>
    /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
    /// <returns>A result interpreted from the symbol "#origin#".</returns>
    public string Generate(int? randomSeed = null) {
      var result = Resolve("#origin#", randomSeed);
      saveData.Clear();
      return result;
    }

    /// <summary>
    /// Resolves all the replacements in a given string with the corresponding saved data or grammar rules.
    /// </summary>
    /// <param name="input">String in which to resolve symbols.</param>
    /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
    /// <returns>The interpreted string according to saved and grammar data.</returns>
    public string Resolve(string input, int? randomSeed = null) {
      if (randomSeed.HasValue) {
        JSONObjectExtensions.SeedRandom(randomSeed.Value);
      }

      var openMatch = open_tag_regex.Match(input);
      if (!openMatch.Success) {
        return input;
      }

      var prefix = input.Substring(0, openMatch.Index);
      var rest = input.Substring(openMatch.Index);

      if (openMatch.Groups["tag"].Value == "[") {
        return prefix + ResolveAction(rest);
      } else if (openMatch.Groups["tag"].Value == "#") {
        return prefix + ResolveTag(rest);
      }

      // Should never get here, but return input as-is if we do.
      return input;
    }

    /// <summary>
    /// Resolve the action match found within the input string.
    /// </summary>
    /// <param name="input">Original input string.</param>
    /// <returns>The input string with the match replaced by its resolved output.</returns>
    private string ResolveAction(string input) {
      var match = action_regex.Match(input);
      if (!match.Success) {
        // No match starting here, shave off the open tag character and resolve the rest.
        return input.Substring(0, 1) + Resolve(input.Substring(1));
      }
      var actionKey = PushAction(match.Groups["content"]);
      var output = Resolve(input.Substring(match.Index + match.Value.Length));
      PopAction(actionKey);
      return output;
    }

    /// <summary>
    /// Resolve the tag match found within the input string.
    /// </summary>
    /// <param name="input">Original input string.</param>
    /// <returns>The input string with the match replaced by its resolved output.</returns>
    private string ResolveTag(string input) {
      var match = tag_regex.Match(input);
      if (!match.Success) {
        // No match starting here, shave off the open tag character and resolve the rest.
        return input.Substring(0, 1) + Resolve(input.Substring(1));
      }
      // Resolve the inner text, which may contain nested actions and tags.
      var innerText = Resolve(match.Groups["content"].Value);
      // To find modifiers, split string on non-escaped '.'s.
      var modifiers = modifier_regex.Split(innerText);
      modifiers[0] = ResolveSymbol(modifiers[0]);
      innerText = ApplyModifiers(modifiers);
      return innerText + Resolve(input.Substring(match.Value.Length));
    }

    /// <summary>
    /// Add a modifier to the modifier lookup.
    /// </summary>
    /// <param name="name">The name to identify the modifier with.</param>
    /// <param name="func">A method that returns a string and takes a string as a param.</param>
    public void AddModifier(string name, Func<string, string> func) {
      ModifierLookup[name] = func;
    }

    /// <summary>
    /// Applies found modifier functions to a symbol.
    /// </summary>
    /// <param name="modifiers">First entry is text to modify. All others are modifiers.</param>
    /// <return>The first entry modified by modifiers found.</return>
    private string ApplyModifiers(string[] modifiers) {
      var baseText = modifiers[0];
      for (var i = 1; i < modifiers.Length; i++) {
        modifiers[0] = ModifierLookup[modifiers[i]](baseText);
      }
      return baseText;
    }


    /// <summary>
    /// Resolve the symbol from save data first, then a grammar rule if nothing saved,
    /// or finally just return the string itself if no saved or grammar substitution.
    /// </summary>
    /// <param name="symbol">The string to match against.</param>
    /// <returns>The interpreted string from save data or grammar, if any.</returns>
    private string ResolveSymbol(string symbol) {
      if (saveData[symbol]) {
        return saveData[symbol][saveData[symbol].Count - 1].Random().str;
      }
      if (grammar[symbol] != null) {
        if (grammar[symbol].IsArray) {
          return grammar[symbol].Random().str;
        } else if (grammar[symbol].IsString) {
          return grammar[symbol].str;
        }
      }
      return symbol;
    }

    /// <summary>
    /// Resolves and saves the content inside [action] brackets.
    /// </summary>
    /// <param name="action">Action group caught in action_regex.</param>
    /// <returns>The key data was saved from within Group, if any.</returns>
    private string PushAction(Group action) {
      if (!action.Success || action.Length == 0) {
        return null;
      }

      var resolved = Resolve(action.Value);
      var match = action_components_regex.Match(resolved);
      // If it's just [content] (without a separating ":"), don't save anything.
      if (!match.Success || !match.Groups["postkey"].Success) {
        return null;
      }

      var key = match.Groups["key"].Value;
      var options = match.Groups["options"].Captures;

      // If action is "POP", pop the selected key instead of saving it.
      if (options.Count == 1 && options[0].Value == "POP") {
        PopAction(key);
        return null;
      }

      JSONObject values = JSONObject.arr;
      foreach (var option in options) {
        values.Add(Resolve(option));
      }

      if (!saveData[key]) {
        saveData[key] = JSONObject.arr;
      }
      saveData[key].Add(values);
    }

    /// <summary>
    /// Pops the most recent entry for the save data key.
    /// </summary>
    /// <param name="key">Names of action to pop.</param>
    private void PopAction(string key) {
      if (key == null) {
        return;
      }
      if (!saveData[key]) {
        return;
      }
      saveData[key].RemoveAt(saveData[key].Count - 1);
      if (saveData[key].Count == 0) {
        saveData.RemoveField(key);
      }
    }

    private bool ParseRulesJson() {
      grammar = new JSONObject(rawGrammar);
      if (!grammar.IsObject) {
        grammar = null;
        return false;
      }
      UnityEngine.Debug.Log("Parsed grammar:\n" + grammar);
      return true;
    }

    /// <summary>
    /// Creates a string for use in a regex that enforces only even number of escape
    /// character '\' by opening a Group every time an odd-number '\' is matched and
    /// closing the Group when an even one is matched.
    /// </summary>
    /// <remarks>
    /// {0}         Evaluates to the provided prefix + `esc`, or empty if none.
    ///             This allows multiple uses of this in the same repeated group
    ///             such as for left and right braces.
    /// Broken down:
    /// (?<=              Start positive lookbehind.
    ///   (?:\\           Start non-capturing Group followed by an escape character '\'.
    ///     (?({0})       Conditional: if the `{0}` Group is open:
    ///       (?<-{0}>)   Close the Group `{0}`.
    ///     |             Otherwise
    ///       (?<{0}>)    Open the Group `{0}`.
    ///     )
    ///   )*              Match any number of times to capture all leading '\'s.
    /// )                 End lookbehind.
    /// (?({0}))          Conditional: if `{0}` Group is open:
    ///   (?!)            Fail the match.
    /// )
    /// </remarks>
    /// </summary>
    /// <param name="match">The content that should be preceeded only by even number of '\' characters.</param>
    /// <param name="prefix">Prefix to the `esc` Group. Use to include multiple unescape sequences in same section.</param>
    /// <returns>Regex-ready string that adds lookbehind for even number of '\' characters.</returns>
    private static string EnforceNonEscaped(string match, string prefix="") {
      return string.Format(@"(?<=(?:\\(?({{0}})(?<-{{0}}>)|(?<{{0}}>)))*)(?({{0}})(?!)){{1}}", prefix + "esc", match);
    }

    /// <summary>
    /// Like EnforceNonEscaped, but instead enforces positive lookbehind for an
    /// odd number of '\' characters.
    /// </summary>
    /// <param name="match">The content that must be escaped.</param>
    /// <param name="prefix">Prefix to the `esc` Group. Use to include multiple escape sequences in same section.</param>
    /// <returns>Regex-ready string that adds lookbehind for odd number of '\' characters.</returns>
    private static string EnforceEscaped(string match, string prefix="") {
      return string.Format(@"(?<=(?:\\(?({{0}})(?<-{{0}}>)|(?<{{0}}>)))*)(?({{0}})(?<-{{0}})|(?!)){{1}}", prefix + "esc", match);
    }

    /// <summary>
    /// Creates a regex which captures between two tags and balances open and close
    /// brackets '[]' in between them.
    /// </summary>
    /// <remarks>
    /// The regexes for capturing actions and symbols are so similar, differing
    /// only in the open/close tags, no reason to define the entire thing twice.
    /// </remarks>
    /// <param name="openTag">Tag to open regex group.</param>
    /// <param name="closeTag">Tag to close regex group.</param>
    /// <returns>A regex for capturing bracket-balanced content between two tags.</returns>
    private static Regex CreateBalancedRegex(string openTag, string closeTag) {
      return new Regex(string.Format(@"
          ^{{0}}                # Match EnforceNonEscaped(openTag).
          (?<content>           # Capture the following in the `content` Group.
            (?:                 # Non capturing group: match the following 3 items...
              (?<BR>{{1}}\[)    # 1. Open Group `BR` when non-escaped `[` is matched (EnforceNonEscaped(`[`, `l`)).
              |(?<-BR>{{2}}\])  # 2. Close Group `BR` when non-escaped `]` is matched (EnforceNonEscaped(`]`, `r`)).
              |[^\[\]]*         # 3. Match any non-bracket character.
            )*?                 # ...any number of times, as few times as possible.
            (?(BR)(?!))         # If Group `BR` is open, fail match (balance open and close brackets within Group `capture`).
          )                     # End capture Group `content`.
          {{3}}                 # Match EnforceNonEscaped(closeTag)",
          EnforceNonEscaped(openTag),
          EnforceNonEscaped(@"\[", "l"),
          EnforceNonEscaped(@"\]", "r"),
          EnforceNonEscaped(closeTag)), RegexOptions.IgnorePatternWhitespace);
    }
  }
}
