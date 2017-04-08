using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Tango;
using UnityEngine;
using UnityEngine.EventSystems;

public class FillAreaController : MonoBehaviour, ControllerInterface, ITangoPose, ITangoEvent, ITangoDepth
{

    /// <summary>
    /// Prefabs of different colored markers.
    /// </summary>
    public GameObject[] m_markPrefabs;

    /// <summary>
    /// The point cloud object in the scene.
    /// </summary>
    public TangoPointCloud m_pointCloud;

    /// <summary>
    /// The canvas to place 2D game objects under.
    /// </summary>
    public Canvas m_canvas;

    /// <summary>
    /// The touch effect to place on taps.
    /// </summary>
    public RectTransform m_prefabTouchEffect;

    /// <summary>
    /// Saving progress UI text.
    /// </summary>
    public UnityEngine.UI.Text m_savingText;

    /// <summary>
    /// The Area Description currently loaded in the Tango Service.
    /// </summary>
    [HideInInspector]
    public AreaDescription m_curAreaDescription;

#if UNITY_EDITOR
    /// <summary>
    /// Handles GUI text input in Editor where there is no device keyboard.
    /// If true, text input for naming new saved Area Description is displayed.
    /// </summary>
    private bool m_displayGuiTextInput;

    /// <summary>
    /// Handles GUI text input in Editor where there is no device keyboard.
    /// Contains text data for naming new saved Area Descriptions.
    /// </summary>
    private string m_guiTextInputContents;

    /// <summary>
    /// Handles GUI text input in Editor where there is no device keyboard.
    /// Indicates whether last text input was ended with confirmation or cancellation.
    /// </summary>
    private bool m_guiTextInputResult;
#endif

    /// <summary>
    /// If set, then the depth camera is on and we are waiting for the next depth update.
    /// </summary>
    private bool m_findPlaneWaitingForDepth;

    /// <summary>
    /// A reference to TangoARPoseController instance.
    /// 
    /// In this class, we need TangoARPoseController reference to get the timestamp and pose when we place a marker.
    /// The timestamp and pose is used for later loop closure position correction. 
    /// </summary>
    private TangoARPoseController m_poseController;

    /// <summary>
    /// List of markers placed in the scene.
    /// </summary>
    private List<GameObject> m_markerList = new List<GameObject>();

    /// <summary>
    /// Reference to the newly placed marker.
    /// </summary>
    private GameObject newMarkObject = null;

    /// <summary>
    /// Current marker type.
    /// </summary>
    private int m_currentMarkType = 0;

    /// <summary>
    /// If set, this is the selected marker.
    /// </summary>
    private ARMarker m_selectedMarker;

    /// <summary>
    /// If set, this is the rectangle bounding the selected marker.
    /// </summary>
    private Rect m_selectedRect;

    /// <summary>
    /// If the interaction is initialized.
    /// 
    /// Note that the initialization is triggered by the relocalization event. We don't want user to place object before
    /// the device is relocalized.
    /// </summary>
    private bool m_initialized = false;

    /// <summary>
    /// A reference to TangoApplication instance.
    /// </summary>
    private TangoApplication m_tangoApplication;

    private Thread m_saveThread;

    public void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && m_initialized)
        {
            // When application is backgrounded, we reload the level because the Tango Service is disconected. All
            // learned area and placed marker should be discarded as they are not saved.
#pragma warning disable 618
            Application.LoadLevel(Application.loadedLevel);
#pragma warning restore 618
        }
    }

    public void OnGUI()
    {
        if (m_selectedMarker != null)
        {
            Renderer selectedRenderer = m_selectedMarker.GetComponent<Renderer>();

            // GUI's Y is flipped from the mouse's Y
            Rect screenRect = _WorldBoundsToScreen(Camera.main, selectedRenderer.bounds);
            float yMin = Screen.height - screenRect.yMin;
            float yMax = Screen.height - screenRect.yMax;
            screenRect.yMin = Mathf.Min(yMin, yMax);
            screenRect.yMax = Mathf.Max(yMin, yMax);

            if (GUI.Button(screenRect, "<size=30>Hide</size>"))
            {
                m_markerList.Remove(m_selectedMarker.gameObject);
                m_selectedMarker.SendMessage("Hide");
                m_selectedMarker = null;
                m_selectedRect = new Rect();
            }
            else
            {
                m_selectedRect = screenRect;
            }
        }
        else
        {
            m_selectedRect = new Rect();
        }

#if UNITY_EDITOR
        // Handle text input when there is no device keyboard in the editor.
        if (m_displayGuiTextInput)
        {
            Rect textBoxRect = new Rect(100,
                                        Screen.height - 200,
                                        Screen.width - 200,
                                        100);

            Rect okButtonRect = textBoxRect;
            okButtonRect.y += 100;
            okButtonRect.width /= 2;

            Rect cancelButtonRect = okButtonRect;
            cancelButtonRect.x = textBoxRect.center.x;

            GUI.SetNextControlName("TextField");
            GUIStyle customTextFieldStyle = new GUIStyle(GUI.skin.textField);
            customTextFieldStyle.alignment = TextAnchor.MiddleCenter;
            m_guiTextInputContents =
                GUI.TextField(textBoxRect, m_guiTextInputContents, customTextFieldStyle);
            GUI.FocusControl("TextField");

            if (GUI.Button(okButtonRect, "OK")
                || (Event.current.type == EventType.keyDown && Event.current.character == '\n'))
            {
                m_displayGuiTextInput = false;
                m_guiTextInputResult = true;
            }
            else if (GUI.Button(cancelButtonRect, "Cancel"))
            {
                m_displayGuiTextInput = false;
                m_guiTextInputResult = false;
            }
        }
#endif
    }

    /// <summary>
    /// Save the game.
    /// 
    /// Save will trigger 3 things:
    /// 
    /// 1. Save the Area Description if the learning mode is on.
    /// 2. Bundle adjustment for all marker positions, please see _UpdateMarkersForLoopClosures() function header for 
    ///     more details.
    /// 3. Save all markers to xml, save the Area Description if the learning mode is on.
    /// 4. Reload the scene.
    /// </summary>
    public void Save()
    {
        StartCoroutine(_DoSaveCurrentAreaDescription());
    }

    /// <summary>
    /// Actually do the Area Description save.
    /// </summary>
    /// <returns>Coroutine IEnumerator.</returns>
    private IEnumerator _DoSaveCurrentAreaDescription()
    {
#if UNITY_EDITOR
        // Work around lack of on-screen keyboard in editor:
        if (m_displayGuiTextInput || m_saveThread != null)
        {
            yield break;
        }

        m_displayGuiTextInput = true;
        m_guiTextInputContents = "Unnamed";
        while (m_displayGuiTextInput)
        {
            yield return null;
        }

        bool saveConfirmed = m_guiTextInputResult;
#else
        if (TouchScreenKeyboard.visible || m_saveThread != null)
        {
            yield break;
        }
        
        TouchScreenKeyboard kb = TouchScreenKeyboard.Open("Unnamed");
        while (!kb.done && !kb.wasCanceled)
        {
            yield return null;
        }

        bool saveConfirmed = kb.done;
#endif
        if (saveConfirmed)
        {
            // Disable interaction before saving.
            m_initialized = false;
            m_savingText.gameObject.SetActive(true);
            if (m_tangoApplication.m_areaDescriptionLearningMode)
            {
                m_saveThread = new Thread(delegate ()
                {
                    // Start saving process in another thread.
                    m_curAreaDescription = AreaDescription.SaveCurrent();
                    AreaDescription.Metadata metadata = m_curAreaDescription.GetMetadata();
#if UNITY_EDITOR
                    metadata.m_name = m_guiTextInputContents;
#else
                    metadata.m_name = kb.text;
#endif
                    m_curAreaDescription.SaveMetadata(metadata);
                });
                m_saveThread.Start();
            }
            else
            {
                _SaveMarkerToDisk();
#pragma warning disable 618
                Application.LoadLevel(Application.loadedLevel);
#pragma warning restore 618
            }
        }
    }

    public void OnTangoDepthAvailable(TangoUnityDepth tangoDepth)
    {
        // Don't handle depth here because the PointCloud may not have been updated yet.  Just
        // tell the coroutine it can continue.
        m_findPlaneWaitingForDepth = false;
    }

    public void OnTangoEventAvailableEventHandler(TangoEvent tangoEvent)
    {
        {
            // We will not have the saving progress when the learning mode is off.
            if (!m_tangoApplication.m_areaDescriptionLearningMode)
            {
                return;
            }

            if (tangoEvent.type == TangoEnums.TangoEventType.TANGO_EVENT_AREA_LEARNING
                && tangoEvent.event_key == "AreaDescriptionSaveProgress")
            {
                m_savingText.text = "Saving. " + (float.Parse(tangoEvent.event_value) * 100) + "%";
            }
        }
    }

    public void OnTangoPoseAvailable(TangoPoseData poseData)
    {
        // This frame pair's callback indicates that a loop closure or relocalization has happened. 
        //
        // When learning mode is on, this callback indicates the loop closure event. Loop closure will happen when the
        // system recognizes a pre-visited area, the loop closure operation will correct the previously saved pose 
        // to achieve more accurate result. (pose can be queried through GetPoseAtTime based on previously saved
        // timestamp).
        // Loop closure definition: https://en.wikipedia.org/wiki/Simultaneous_localization_and_mapping#Loop_closure
        //
        // When learning mode is off, and an Area Description is loaded, this callback indicates a
        // relocalization event. Relocalization is when the device finds out where it is with respect to the loaded
        // Area Description. In our case, when the device is relocalized, the markers will be loaded because we
        // know the relatvie device location to the markers.
        if (poseData.framePair.baseFrame ==
            TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_AREA_DESCRIPTION &&
            poseData.framePair.targetFrame ==
            TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_START_OF_SERVICE &&
            poseData.status_code == TangoEnums.TangoPoseStatusType.TANGO_POSE_VALID)
        {
            // When we get the first loop closure/ relocalization event, we initialized all the in-game interactions.
            if (!m_initialized)
            {
                m_initialized = true;
                if (m_curAreaDescription == null)
                {
                    Debug.Log("AndroidInGameController.OnTangoPoseAvailable(): m_curAreaDescription is null");
                    return;
                }

                _LoadMarkerFromDisk();
            }
        }
    }

    public void _LoadMarkerFromDisk()
    {
        // Attempt to load the exsiting markers from storage.
        string path = Application.persistentDataPath + "/" + m_curAreaDescription.m_uuid + ".xml";

        var serializer = new XmlSerializer(typeof(List<MarkerData>));
        var stream = new FileStream(path, FileMode.Open);

        List<MarkerData> xmlDataList = serializer.Deserialize(stream) as List<MarkerData>;

        if (xmlDataList == null)
        {
            Debug.Log("AndroidInGameController._LoadMarkerFromDisk(): xmlDataList is null");
            return;
        }

        m_markerList.Clear();
        foreach (MarkerData mark in xmlDataList)
        {
            // Instantiate all markers' gameobject.
            GameObject temp = Instantiate(m_markPrefabs[mark.m_type],
                                          mark.m_position,
                                          mark.m_orientation) as GameObject;
            m_markerList.Add(temp);
        }
    }

    public void _SaveMarkerToDisk()
    {
        // Compose a XML data list.
        List<MarkerData> xmlDataList = new List<MarkerData>();
        foreach (GameObject obj in m_markerList)
        {
            // Add marks data to the list, we intentionally didn't add the timestamp, because the timestamp will not be
            // useful when the next time Tango Service is connected. The timestamp is only used for loop closure pose
            // correction in current Tango connection.
            MarkerData temp = new MarkerData();
            temp.m_type = obj.GetComponent<ARMarker>().m_type;
            temp.m_position = obj.transform.position;
            temp.m_orientation = obj.transform.rotation;
            xmlDataList.Add(temp);
        }

        string path = Application.persistentDataPath + "/" + m_curAreaDescription.m_uuid + ".xml";
        var serializer = new XmlSerializer(typeof(List<MarkerData>));
        using (var stream = new FileStream(path, FileMode.Create))
        {
            serializer.Serialize(stream, xmlDataList);
        }
    }

    public void _UpdateMarkersForLoopClosures()
    {
        // Adjust mark's position each time we have a loop closure detected.
        foreach (GameObject obj in m_markerList)
        {
            ARMarker tempMarker = obj.GetComponent<ARMarker>();
            if (tempMarker.m_timestamp != -1.0f)
            {
                TangoCoordinateFramePair pair;
                TangoPoseData relocalizedPose = new TangoPoseData();

                pair.baseFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_AREA_DESCRIPTION;
                pair.targetFrame = TangoEnums.TangoCoordinateFrameType.TANGO_COORDINATE_FRAME_DEVICE;
                PoseProvider.GetPoseAtTime(relocalizedPose, tempMarker.m_timestamp, pair);

                Matrix4x4 uwTDevice = m_poseController.m_uwTss
                                      * relocalizedPose.ToMatrix4x4()
                                      * m_poseController.m_dTuc;

                Matrix4x4 uwTMarker = uwTDevice * tempMarker.m_deviceTMarker;

                obj.transform.position = uwTMarker.GetColumn(3);
                obj.transform.rotation = Quaternion.LookRotation(uwTMarker.GetColumn(2), uwTMarker.GetColumn(1));
            }
        }
    }

    public IEnumerator _WaitForDepthAndFindPlane(Vector2 touchPosition)
    {
        m_findPlaneWaitingForDepth = true;

        // Turn on the camera and wait for a single depth update.
        m_tangoApplication.SetDepthCameraRate(TangoEnums.TangoDepthCameraRate.MAXIMUM);
        while (m_findPlaneWaitingForDepth)
        {
            yield return null;
        }

        m_tangoApplication.SetDepthCameraRate(TangoEnums.TangoDepthCameraRate.DISABLED);

        // Find the plane.
        Camera cam = Camera.main;
        Vector3 planeCenter;
        Plane plane;
        if (!m_pointCloud.FindPlane(cam, touchPosition, out planeCenter, out plane))
        {
            yield break;
        }

        // Ensure the location is always facing the camera.  This is like a LookRotation, but for the Y axis.
        Vector3 up = plane.normal;
        Vector3 forward;
        if (Vector3.Angle(plane.normal, cam.transform.forward) < 175)
        {
            Vector3 right = Vector3.Cross(up, cam.transform.forward).normalized;
            forward = Vector3.Cross(right, up).normalized;
        }
        else
        {
            // Normal is nearly parallel to camera look direction, the cross product would have too much
            // floating point error in it.
            forward = Vector3.Cross(up, cam.transform.right);
        }

        // Instantiate marker object.
        newMarkObject = Instantiate(m_markPrefabs[m_currentMarkType],
                                    planeCenter,
                                    Quaternion.LookRotation(forward, up)) as GameObject;

        ARMarker markerScript = newMarkObject.GetComponent<ARMarker>();

        markerScript.m_type = m_currentMarkType;
        markerScript.m_timestamp = (float)m_poseController.m_poseTimestamp;

        Matrix4x4 uwTDevice = Matrix4x4.TRS(m_poseController.m_tangoPosition,
                                            m_poseController.m_tangoRotation,
                                            Vector3.one);
        Matrix4x4 uwTMarker = Matrix4x4.TRS(newMarkObject.transform.position,
                                            newMarkObject.transform.rotation,
                                            Vector3.one);
        markerScript.m_deviceTMarker = Matrix4x4.Inverse(uwTDevice) * uwTMarker;

        m_markerList.Add(newMarkObject);

        m_selectedMarker = null;
    }

    public Rect _WorldBoundsToScreen(Camera cam, Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        Bounds screenBounds = new Bounds(cam.WorldToScreenPoint(center), Vector3.zero);

        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(+extents.x, +extents.y, +extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(+extents.x, +extents.y, -extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(+extents.x, -extents.y, +extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(+extents.x, -extents.y, -extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(-extents.x, +extents.y, +extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(-extents.x, +extents.y, -extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(-extents.x, -extents.y, +extents.z)));
        screenBounds.Encapsulate(cam.WorldToScreenPoint(center + new Vector3(-extents.x, -extents.y, -extents.z)));
        return Rect.MinMaxRect(screenBounds.min.x, screenBounds.min.y, screenBounds.max.x, screenBounds.max.y);
    }



    /// <summary>
    /// Data container for marker.
    /// 
    /// Used for serializing/deserializing marker to xml.
    /// </summary>
    [System.Serializable]
    public class MarkerData
    {
        /// <summary>
        /// Marker's type.
        /// 
        /// Red, green or blue markers. In a real game scenario, this could be different game objects
        /// (e.g. banana, apple, watermelon, persimmons).
        /// </summary>
        [XmlElement("type")]
        public int m_type;

        /// <summary>
        /// Position of the this mark, with respect to the origin of the game world.
        /// </summary>
        [XmlElement("position")]
        public Vector3 m_position;

        /// <summary>
        /// Rotation of the this mark.
        /// </summary>
        [XmlElement("orientation")]
        public Quaternion m_orientation;
    }


    public void SetCurrentMarkType(int type)
    {
        if (type != m_currentMarkType)
        {
            m_currentMarkType = type;
        }
    }

    // Use this for initialization
    public void Start () 
    {
        m_poseController = FindObjectOfType<TangoARPoseController>();
        m_tangoApplication = FindObjectOfType<TangoApplication>();

        if (m_tangoApplication != null)
        {
            m_tangoApplication.Register(this);
        }
    }


    // Update is called once per frame
    void Update () {
        {
            //Debug.Log("hasdfhashdfs");

            if (m_saveThread != null && m_saveThread.ThreadState != ThreadState.Running)
            {
                // After saving an Area Description or mark data, we reload the scene to restart the game.
                _UpdateMarkersForLoopClosures();
                _SaveMarkerToDisk();
#pragma warning disable 618
                Application.LoadLevel(Application.loadedLevel);
#pragma warning restore 618
            }

            if (Input.GetKey(KeyCode.Escape))
            {
#pragma warning disable 618
                Application.LoadLevel(Application.loadedLevel);
#pragma warning restore 618
            }

            if (!m_initialized)
            {
                return;
            }

            if (EventSystem.current.IsPointerOverGameObject(0) || GUIUtility.hotControl != 0)
            {
                return;
            }

            if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                Vector2 guiPosition = new Vector2(t.position.x, Screen.height - t.position.y);
                Camera cam = Camera.main;
                RaycastHit hitInfo;

                if (t.phase != TouchPhase.Began)
                {
                    return;
                }

                if (m_selectedRect.Contains(guiPosition))
                {
                    // do nothing, the button will handle it
                }
                else if (Physics.Raycast(cam.ScreenPointToRay(t.position), out hitInfo))
                {
                    // Found a marker, select it (so long as it isn't disappearing)!
                    GameObject tapped = hitInfo.collider.gameObject;
                    if (!tapped.GetComponent<Animation>().isPlaying)
                    {
                        m_selectedMarker = tapped.GetComponent<ARMarker>();
                    }
                }
                else
                {
                    // Place a new point at that location, clear selection
                    m_selectedMarker = null;
                    StartCoroutine(_WaitForDepthAndFindPlane(t.position));

                    // Because we may wait a small amount of time, this is a good place to play a small
                    // animation so the user knows that their input was received.
                    RectTransform touchEffectRectTransform = Instantiate(m_prefabTouchEffect) as RectTransform;
                    touchEffectRectTransform.transform.SetParent(m_canvas.transform, false);
                    Vector2 normalizedPosition = t.position;
                    normalizedPosition.x /= Screen.width;
                    normalizedPosition.y /= Screen.height;
                    touchEffectRectTransform.anchorMin = touchEffectRectTransform.anchorMax = normalizedPosition;
                }
            }
        }
    }

    void ControllerInterface.Update()
    {
        throw new NotImplementedException();
    }
}
