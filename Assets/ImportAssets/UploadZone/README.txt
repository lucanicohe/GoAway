UNITY 6 / URP — UPLOAD ZONE
1. Copy the UploadZone folder into Assets.
2. Create an empty GameObject at the floor-level center of your upload radius. Add UploadZoneVisual.
3. Drag UploadZone.shader into Zone Shader (important for builds). Set Radius to your actual upload radius in metres.
4. Enter Play mode and change State to Uploading to preview. Enable HDR, camera post-processing and Bloom for glow.
5. In your terminal script add: [SerializeField] private UploadZoneVisual uploadZone;
   Assign the component in the Inspector. Call uploadZone.ShowUploading() when uploading starts/resumes; uploadZone.ShowPaused() when leaving; uploadZone.ShowComplete() once on completion; uploadZone.Hide() on reset.
   If your radius changes, call uploadZone.SetRadius(yourRadius).
The visual uses a horizontal circle, not a sphere; match the gameplay radius and center in XZ. If gameplay uses a 3D sphere, the floor footprint may differ. Raise Floor Offset if the floor flickers. This is a visual only and does not change upload logic. Not compiled or tested in Unity in this environment.
