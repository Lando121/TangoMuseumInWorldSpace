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

public interface ControllerInterface {


    // Use this for initialization
    void Start();

    // Update is called once per frame
    void Update();

    /// <summary>
    /// Application onPause / onResume callback.
    /// </summary>
    /// <param name="pauseStatus"><c>true</c> if the application about to pause, otherwise <c>false</c>.</param>
    void OnApplicationPause(bool pauseStatus);

    /// <summary>
    /// Unity OnGUI function.
    /// 
    /// Mainly for removing markers.
    /// </summary>
    void OnGUI();

    /// <summary>
    /// This is called each time a Tango event happens.
    /// </summary>
    /// <param name="tangoEvent">Tango event.</param>
    void OnTangoEventAvailableEventHandler(Tango.TangoEvent tangoEvent);

    /// <summary>
    /// OnTangoPoseAvailable event from Tango.
    /// 
    /// In this function, we only listen to the Start-Of-Service with respect to Area-Description frame pair. This pair
    /// indicates a relocalization or loop closure event happened, base on that, we either start the initialize the
    /// interaction or do a bundle adjustment for all marker position.
    /// </summary>
    /// <param name="poseData">Returned pose data from TangoService.</param>
    void OnTangoPoseAvailable(Tango.TangoPoseData poseData);

    /// <summary>
    /// This is called each time new depth data is available.
    /// 
    /// On the Tango tablet, the depth callback occurs at 5 Hz.
    /// </summary>
    /// <param name="tangoDepth">Tango depth.</param>
    void OnTangoDepthAvailable(TangoUnityDepth tangoDepth);

    /// <summary>
    /// Correct all saved marks when loop closure happens.
    /// 
    /// When Tango Service is in learning mode, the drift will accumulate overtime, but when the system sees a
    /// preexisting area, it will do a operation to correct all previously saved poses
    /// (the pose you can query with GetPoseAtTime). This operation is called loop closure. When loop closure happens,
    /// we will need to re-query all previously saved marker position in order to achieve the best result.
    /// This function is doing the querying job based on timestamp.
    /// </summary>
    void _UpdateMarkersForLoopClosures();

    /// <summary>
    /// Write marker list to an xml file stored in application storage.
    /// </summary>
    void _SaveMarkerToDisk();

    /// <summary>
    /// Load marker list xml from application storage.
    /// </summary>
    void _LoadMarkerFromDisk();

    /// <summary>
    /// Convert a 3D bounding box represented by a <c>Bounds</c> object into a 2D 
    /// rectangle represented by a <c>Rect</c> object.
    /// </summary>
    /// <returns>The 2D rectangle in Screen coordinates.</returns>
    /// <param name="cam">Camera to use.</param>
    /// <param name="bounds">3D bounding box.</param>
    Rect _WorldBoundsToScreen(Camera cam, Bounds bounds);

    /// <summary>
    /// Wait for the next depth update, then find the plane at the touch position.
    /// </summary>
    /// <returns>Coroutine IEnumerator.</returns>
    /// <param name="touchPosition">Touch position to find a plane at.</param>
    IEnumerator _WaitForDepthAndFindPlane(Vector2 touchPosition);

}
