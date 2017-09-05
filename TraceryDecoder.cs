using System.Collections;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using LitJson;
using TraceryNet;

public class TraceryGrammar {
  /// <summary>
  /// Modifier function table.
  /// </summary>
  public Dictionary<string, Func<string, string>> ModifierLookup;

  /// <summary>
  /// RNG to pick from multiple rules.
  /// </summary>
  private Random Random = new Random();

  public string source;
  Dictionary<string, string[]> grammar;

  private readonly Regex rgx = new Regex(@"(?<!\[|:)(?!\])#.+?(?<!\[|:)#(?!\])", RegexOptions.IgnoreCase);

  [ThreadStatic] private static System.Random random;

  public string Generate(int? randomSeed = null) {
    return GenerateFromNode("origin", randomSeed);
  }

  public string GenerateFromNode(string token, int? randomSeed = null) {
    if (randomSeed.HasValue) {
      random = new System.Random(randomSeed.Value);
    }
    // Find modifiers.

    var matchName = token.Replace("#", "");

    if (matchName.Contains(".")) {
      matchName = matchName.Substring(0, matchName.IndexOf(".", StringComparison.Ordinal));
    }

    if (grammar.ContainsKey(matchName)) {
      var modifiers = GetModifiers(token);
      var resolved = grammar[matchName][Random.Next(grammar[matchName].Length)];
      resolved = rgx.Replace(resolved, m => GenerateFromNode(m.Groups[0].Value));
      if (modifiers.Count == 0) {
        return resolved;
      }

      resolved = ApplyModifiers(resolved, modifiers);
      return resolved;
    } else {
      return "[" + token + "]";
    }
  }

  /// <summary>
  /// Return a list of modifier names from the provided expansion symbol
  /// Modifiers are extra operations to perform on an expansion symbol.
  ///
  /// For instance:
  ///      #animal.capitalize#
  /// will flatten into a single animal and capitalize the first character of it's name.
  ///
  /// Multiple modifiers can be applied, separated by a .
  ///      #animal.capitalize.inQuotes#
  /// ...for example
  /// </summary>
  /// <param name="symbol">The symbol to take modifiers from:
  /// e.g: #animal#, #animal.s#, #animal.capitalize.s#
  /// </param>
  /// <returns></returns>
  private List<string> GetModifiers(string symbol) {
    var modifiers = symbol.Replace("#", "").Split('.').ToList();
    modifiers.RemoveAt(0);

    return modifiers;
  }


  public static List<T> Shuffle<T>(IEnumerable<T> list) {
    var rand = random ??
               (random = new System.Random(unchecked(Environment.TickCount * 31 +
                                                     System.Threading.Thread.CurrentThread.ManagedThreadId)));

    var output = new List<T>(list);
    int n = output.Count;
    while (n > 1) {
      n--;
      int k = rand.Next(n + 1);
      T value = output[k];
      output[k] = output[n];
      output[n] = value;
    }
    return output;
  }


  Dictionary<string, string[]> Decode(string source) {
    Dictionary<string, string[]> traceryStruct = new Dictionary<string, string[]>();
    var map = JsonToMapper(source);
    foreach (var key in map.Keys) {
      if (map[key].IsArray) {
        string[] entries = new string[map[key].Count];
        for (int i = 0; i < map[key].Count; i++) {
          var entry = map[key][i];
          entries[i] = (string) entry;
        }
        traceryStruct.Add(key, entries);
      } else if (map[key].IsString) {
        string[] entries = {map[key].ToString()};
        traceryStruct.Add(key, entries);
      }
    }
    return traceryStruct;
  }

  public static JsonData JsonToMapper(string tracery) {
    var traceryStructure = JsonMapper.ToObject(tracery);
    return traceryStructure;
  }

  /// <summary>
  /// Add a modifier to the modifier lookup.
  /// </summary>
  /// <param name="name">The name to identify the modifier with.</param>
  /// <param name="func">A method that returns a string and takes a string as a param.</param>
  public void AddModifier(string name, Func<string, string> func) {
    ModifierLookup[name] = func;
  }

  public TraceryGrammar(string source) {
    this.source = source;
    this.grammar = Decode(source);

    // Set up the function table
    ModifierLookup = new Dictionary<string, Func<string, string>> {
      {"a", Modifiers.A},
      {"beeSpeak", Modifiers.BeeSpeak},
      {"capitalize", Modifiers.Capitalize},
      {"capitalizeAll", Modifiers.CapitalizeAll},
      {"comma", Modifiers.Comma},
      {"inQuotes", Modifiers.InQuotes},
      {"s", Modifiers.S},
      {"ed", Modifiers.Ed},
      {"titleCase", Modifiers.TitleCase}
    };
  }

  /// <summary>
  /// Iterate over the list of modifiers on the expansion symbol and resolve each individually.
  /// </summary>
  /// <param name="resolved">The string to perform the modifications to</param>
  /// <param name="modifiers">The list of modifier strings</param>
  /// <returns>The resolved string with modifiers applied to it</returns>
  private string ApplyModifiers(string resolved, List<string> modifiers) {
    // Iterate over each modifier
    foreach (var modifier in modifiers) {
      // If there's no modifier by this name in the list, skip it
      if (!ModifierLookup.ContainsKey(modifier))
        continue;

      // Otherwise execute the function and take the output
      resolved = ModifierLookup[modifier](resolved);
    }

    // Give back the string
    return resolved;
  }
}