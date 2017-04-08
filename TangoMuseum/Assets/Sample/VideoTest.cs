﻿using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

public class VideoTest : MonoBehaviour {

	WebGLMovieTexture tex;
	public GameObject cube;

	void Start () {
		tex = new WebGLMovieTexture("StreamingAssets/Chrome_ImF.mp4");
		cube.GetComponent<MeshRenderer>().material = new Material (Shader.Find("Diffuse"));
		cube.GetComponent<MeshRenderer>().material.mainTexture = tex;
	}

	void Update()
	{
		tex.Update();
		cube.transform.Rotate (Time.deltaTime * 10, Time.deltaTime * 30, 0);
	}

	void OnGUI()
	{
		GUI.enabled = tex.isReady;
		
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Play"))
			tex.Play();
		if (GUILayout.Button("Pause"))
			tex.Pause();
		tex.loop = GUILayout.Toggle(tex.loop, "Loop");
		GUILayout.EndHorizontal();

		var oldT = tex.time;
		var newT = GUILayout.HorizontalSlider (tex.time, 0.0f, tex.duration);
		if (!Mathf.Approximately(oldT, newT))
			tex.Seek(newT);

		GUI.enabled = true;
	}
}
