using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityTracery {
  [Serializable]
  public class TraceryGrammar {
    public string rawGrammar;

    private JObject grammar;

    /// <summary>
    /// Regex that matches a replacement symbol within a string, including any
    /// potential actions, symbols, and modifiers. Also catches isolated actions
    /// not part of a #symbol# group.
    ///
    /// E.g. Input string "Uncaptured [soloaction:#soloaction#] content [action0:#action0#][action1:#action1#]#[action2:#action2#][action3:#action3#]symbol.mod1.mod2# ..."
    /// The first match would break down as:
    /// Whole Match: `[action0:action0]`
    /// - Group `actions`:
    /// - - Value: `soloaction:#soloaction#`
    /// - - Captures: [`soloaction:#soloaction#`]
    ///
    /// The second match would break down as:
    /// - Group: `actions`:
    /// - - Value: `action1:#action1#`
    /// - - Captures: [`action0:#action0#]`, `[action1:#action1#`]
    ///
    /// The third and final match would break down as:
    /// Whole Match: `#[action2:#action2#][action3:#action3#]symbol.mod1.mod2#`
    /// - Group `inneractions`:
    /// - - Value: `action3:#action3#`
    /// - - Captures: [`action2:#action2#`, `action3:#action3#`]
    /// - Group `symbol`:
    /// - - Value: `symbol`
    /// - Group `modifiers`:
    /// - - Value: `mod2`
    /// - - Captures: [`mod`, `mod2`]
    /// </summary>
    private static readonly Regex ExpansionRegex = new Regex(@"(?:(?:(?!=#)|(?<!#))(?:\[(?<actions>[^\[\]]+)\])+)|(?:#(?:\[(?<inneractions>[^\[\]]+)\])*(?<symbol>.*?)(?:\.(?<modifiers>[^\[\]\.#]+))*#)");

    /// <summary>
    /// Modifier function table.
    /// </summary>
    public Dictionary<string, Func<string, string>> ModifierLookup;

    /// <summary>
    /// Key/value store for savable data.
    /// </summary>
    private Dictionary<string, List<string>> saveData;

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
      saveData = new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Resolves the default starting symbol "#origin#".
    /// </summary>
    /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
    /// <returns>A result interpreted from the symbol "#origin#".</returns>
    public string Generate(int? randomSeed = null) {
      return Resolve("#origin#", randomSeed);
    }

    /// <summary>
    /// Resolves all the replacements in a given string with the corresponding saved data or grammar rules.
    /// </summary>
    /// <param name="token">String in which to resolve symbols.</param>
    /// <param name="randomSeed">Reliably seeds "random" selection to provide consistent results.</param>
    /// <returns>The interpreted string according to saved and grammar data.</returns>
    public string Resolve(string token, int? randomSeed = null) {
      if (randomSeed.HasValue) {
        JArray.SeedRandom(randomSeed);
      }

      List<string> outerActionKeys = null;

      // Find expansion matches.
      foreach (Match match in ExpansionRegex.Matches(token)) {
        // If `actions` group had any captures, this is an isolated action set.
        // Actions registered by themselves apply to the entire token from this point on.
        if (PushActions(match.Groups["actions"].Captures, ref outerActionKeys)) {
          continue;
        }

        // Resolve the symbol from saved data or grammar rules, if any.
        var result = ResolveSymbol(match.Groups["symbol"].Value);

        // Store save symbols within #'s in saveData for this match only.
        List<string> actionKeys = null;
        PushActions(match.Groups["inneractions"].Captures, ref actionKeys);

        // Recursively resolve any nested symbols present in the resolved data.
        result = Resolve(result, randomSeed);

        // Apply modifiers, if any, to the result.
        result = ApplyModifiers(result, match.Groups["modifiers"].Captures);

        // Replace only the first occurance of the match within the input string.
        var first = token.IndexOf(match.Value);
        token = token.Substring(0, first) + result + token.Substring(first + match.Value.Length);

        // Keep saved data in scope of their symbols by popping added keys before continuing.
        PopActions(actionKeys);
      }

      // Now pop the save data applied outside the inner #'s.
      PopActions(outerActionKeys);

      return token;
    }

    /// <summary>
    /// Resolve the symbol from save data first, then a grammar rule if nothing saved,
    /// or finally just return the string itself if no saved or grammar substitution.
    /// </summary>
    /// <param name="symbol">The string to match against.</param>
    /// <returns>The interpreted string from save data or grammar, if any.</returns>
    private string ResolveSymbol(string symbol) {
      if (saveData.ContainsKey(symbol)) {
        return saveData[symbol].Last();
      }
      var rule = grammar[symbol] ?? symbol;
      return rule.Type == JTokenType.Array ? ((JArray)rule).Random : rule;
    }

    /// <summary>
    /// Resolves and saves any actions marked by `actions` captures.
    /// </summary>
    /// <param name="actions">Collection of captures from `actions` Group.</param>
    /// <param name="keyList">List to add action keys to. Initialize if null.</param>
    /// <returns>Whether there were any captures in this group..</returns>
    private bool PushActions(CaptureCollection actions, ref List<string> keyList) {
      if (actions.Count == 0) {
        return false;
      }

      List<string> pushedDataKeys = null;
      foreach (Capture capture in actions) {
        var save = capture.Value;

        // If it's just [#key#], then flatten #key#
        if (!save.Contains(":")) {
          Resolve(string.Format("{0}", save));
          continue;
        }

        // For [key:#symbol#], split into key and data.
        var saveSplit = save.Split(':');
        var key = saveSplit[0];
        var data = Resolve(saveSplit[1]);

        // Save resolution of symbol to key in saveData.
        // If key already exists, push the new value to the end. Otherwise add it.
        if (pushedDataKeys == null) {
          pushedDataKeys = ListPool.GetOrCreateList<string>();
        }
        if (keyList == null) {
          keyList = ListPool.GetOrCreateList<string>();
        }
        pushedDataKeys.Add(key);
        keyList.Add(key);
        if (saveData.ContainsKey(key)) {
          saveData[key].Add(data);
        } else {
          saveData[key] = ListPool.GetOrCreateList<string>();
          saveData[key].Add(data);
        }
      }

      return true;
    }

    /// <summary>
    /// Pops the most recent entry for each action in keys.
    /// Returns any empty lists to ListPool, as well as keys List itself.
    /// </summary>
    /// <param name="keys">Names of actions to pop.</param>
    private void PopActions(List<string> keys) {
      if (keys == null) {
        return;
      }
      foreach (var key in keys) {
        if (!saveData.ContainsKey(key)) {
          continue;
        }
        saveData[key].RemoveAt(saveData[key].Count - 1);
        if (saveData[key].Count == 0) {
          saveData[key].FreeList();
        }
      }
      keys.FreeList();
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
    /// Resolve each defined modifier on the expansion data.
    /// </summary>
    /// <param name="data">The string to modify.</param>
    /// <param name="modifiers">Captured modifier names.</param>
    /// <returns>The resolved data with modifiers applied.</returns>
    private string ApplyModifiers(string data, CaptureCollection modifiers) {
      foreach (Capture modName in modifiers) {
        if (!ModifierLookup.ContainsKey(modName.Value)) {
          continue;
        }
        data = ModifierLookup[modName.Value](data);
      }
      return data;
    }

    private static bool ParseRulesJson() {
      if (rawGrammar.Length == 0) {
        return false;
      }
      if (rawGrammar[0] != '{') {
        return false;
      }

      try {
        var token = JToken.Parse(json);
        grammar = token as JObject;
        return grammar != null;
      } catch (JsonReaderException e) {
        return false;
      }
    }
  }
}
