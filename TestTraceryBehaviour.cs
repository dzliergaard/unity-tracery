using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestTraceryBehaviour : MonoBehaviour {
  public string JsonFileName = "test-grammar.json";
  public TraceryGrammar Grammar;

	// Use this for initialization
	void Start () {
    var grammarPath = Application.dataPath + "/Resources/JSON/" + JsonFileName;
    StreamReader reader = new StreamReader(grammarPath);
    var grammarString = reader.ReadToEnd();
    Debug.Log(grammarString);
    Grammar = new TraceryGrammar(grammarString);
  }

  void Update() {
    if (Input.GetMouseButtonDown(0)) {
      Debug.Log("Generated string: " + Grammar.GenerateFromNode("test"));
    }
  }
}
