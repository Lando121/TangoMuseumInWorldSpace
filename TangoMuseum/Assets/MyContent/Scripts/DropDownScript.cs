using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DropDownScript : MonoBehaviour {

    public GameObject[] m_gameobjects = new GameObject[2];

    public Dropdown dropdown;

	// Use this for initialization
	void Start () {
        dropdown.onValueChanged.AddListener(delegate {
            dropDownValueChangedHandler();
        });
        addOptions();
		
	}
     
    private void addOptions()
    {
        foreach (GameObject gameObject in m_gameobjects)
        {
            string optionTitle = gameObject.GetComponent<ARObject>().title;
            dropdown.options.Add(new Dropdown.OptionData() { text =  optionTitle});
        }
        
    }

    private void dropDownValueChangedHandler()
    {
        GameObject newObject = m_gameobjects[dropdown.value];
        GameObject.Find("UIController").GetComponent<PlacingObjectsController>().m_currentObject = newObject;
    }
	
	// Update is called once per frame
	void Update () {
        

    }
}
