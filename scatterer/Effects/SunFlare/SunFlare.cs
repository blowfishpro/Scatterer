//rendering steps
//scaledSpaceCamera.OnPrecull -> skynodes update the extinction texture one after one
//camerahook on the relevant scaledSpace or farCamera -> clear the extinction texture before the next frame
//						while taking care of keeping it around until the rendering has finished (either nearCamera or scaledCamera)


using UnityEngine;
using System.Collections;
using System.IO;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using KSP.IO;

namespace scatterer
{
	
	public class SunFlare : MonoBehaviour
	{	

		[Persistent]
		public string assetPath;

		public Material sunglareMaterial;

		public CelestialBody source;
		public string sourceName;
		public Transform sourceScaledTransform;

		Texture2D sunSpikes;
		Texture2D sunFlare;

		Texture2D sunGhost1;
		Texture2D sunGhost2;
		Texture2D sunGhost3;

		public RenderTexture extinctionTexture;
		int waitBeforeReloadCnt = 0;
		SunflareCameraHook nearCameraHook, scaledCameraHook;

		Vector3 sunViewPortPos=Vector3.zero;

		RaycastHit hit;
		bool hitStatus=false;
		bool eclipse=false;

		float sunGlareScale=1;
		float sunGlareFade=1;
		float ghostFade=1;

		Mesh screenMesh;
		GameObject sunflareGameObject;

		[Persistent]
		float sunGlareFadeDistance = 250000;
		[Persistent]
		float ghostFadeDistance = 13500000;

		//input settings
		[Persistent]
		Vector3 flareSettings = Vector3.zero;
		[Persistent]
		Vector3 spikesSettings = Vector3.zero;
	
		[Persistent]
		List<Vector4> ghost1SettingsList1=new List<Vector4>{};
		[Persistent]
		List<Vector4> ghost1SettingsList2=new List<Vector4>{};

		[Persistent]
		List<Vector4> ghost2SettingsList1=new List<Vector4>{};
		[Persistent]
		List<Vector4> ghost2SettingsList2=new List<Vector4>{};

		[Persistent]
		List<Vector4> ghost3SettingsList1=new List<Vector4>{};
		[Persistent]
		List<Vector4> ghost3SettingsList2=new List<Vector4>{};


		public void start()
		{
			loadConfigNode ();

			sunglareMaterial = new Material (ShaderReplacer.Instance.LoadedShaders["Scatterer/sunFlare"]);
			sunglareMaterial.SetOverrideTag ("IGNOREPROJECTOR", "True");
			sunglareMaterial.SetOverrideTag ("IgnoreProjector", "True");

			//Size is loaded automatically from the files
			sunSpikes = new Texture2D (1, 1);
			sunFlare = new Texture2D (1, 1);
			sunGhost1 = new Texture2D (1, 1);
			sunGhost2 = new Texture2D (1, 1);
			sunGhost3 = new Texture2D (1, 1);
			
			sunSpikes.LoadImage (System.IO.File.ReadAllBytes (String.Format ("{0}/{1}", Core.Instance.gameDataPath + assetPath, "sunSpikes.png")));
			sunSpikes.wrapMode = TextureWrapMode.Clamp;
			sunFlare.LoadImage (System.IO.File.ReadAllBytes (String.Format ("{0}/{1}", Core.Instance.gameDataPath + assetPath, "sunFlare.png")));
			sunFlare.wrapMode = TextureWrapMode.Clamp;
			sunGhost1.LoadImage (System.IO.File.ReadAllBytes (String.Format ("{0}/{1}", Core.Instance.gameDataPath + assetPath, "Ghost1.png")));
			sunGhost1.wrapMode = TextureWrapMode.Clamp;
			sunGhost2.LoadImage (System.IO.File.ReadAllBytes (String.Format ("{0}/{1}", Core.Instance.gameDataPath + assetPath, "Ghost2.png")));
			sunGhost2.wrapMode = TextureWrapMode.Clamp;
			sunGhost3.LoadImage (System.IO.File.ReadAllBytes (String.Format ("{0}/{1}", Core.Instance.gameDataPath + assetPath, "Ghost3.png")));
			sunGhost3.wrapMode = TextureWrapMode.Clamp;

//			sunglareMaterial.SetTexture ("_Sun_Glare", sunGlare);
			sunglareMaterial.SetTexture ("sunSpikes", sunSpikes);
			sunglareMaterial.SetTexture ("sunFlare", sunFlare);
			sunglareMaterial.SetTexture ("sunGhost1", sunGhost1);
			sunglareMaterial.SetTexture ("sunGhost2", sunGhost2);
			sunglareMaterial.SetTexture ("sunGhost3", sunGhost3);

			if (!(HighLogic.LoadedScene == GameScenes.TRACKSTATION))
				sunglareMaterial.SetTexture ("_customDepthTexture", Core.Instance.bufferRenderingManager.depthTexture);
			else
				sunglareMaterial.SetTexture ("_customDepthTexture", Texture2D.whiteTexture);

			sunglareMaterial.renderQueue = 3100;

			screenMesh = MeshFactory.MakePlane (2, 2, MeshFactory.PLANE.XY, false, false);
			screenMesh.bounds = new Bounds (Vector4.zero, new Vector3 (Mathf.Infinity, Mathf.Infinity, Mathf.Infinity));

			sunflareGameObject = new GameObject ();
			MeshFilter sunflareGameObjectMeshFilter;

			if (sunflareGameObject.GetComponent<MeshFilter> ())
				sunflareGameObjectMeshFilter = sunflareGameObject.GetComponent<MeshFilter> ();
			else
				sunflareGameObjectMeshFilter = sunflareGameObject.AddComponent<MeshFilter>();
			
			sunflareGameObjectMeshFilter.mesh.Clear ();
			sunflareGameObjectMeshFilter.mesh = screenMesh;
			
			MeshRenderer sunflareGameObjectMeshRenderer;

			if (sunflareGameObject.GetComponent<MeshRenderer> ())
				sunflareGameObjectMeshRenderer = sunflareGameObject.GetComponent<MeshRenderer> ();
			else
				sunflareGameObjectMeshRenderer = sunflareGameObject.AddComponent<MeshRenderer>();
			
			sunflareGameObjectMeshRenderer.sharedMaterial = sunglareMaterial;
			sunflareGameObjectMeshRenderer.material = sunglareMaterial;
			
			sunflareGameObjectMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			sunflareGameObjectMeshRenderer.receiveShadows = false;
			sunflareGameObjectMeshRenderer.enabled = true;
			
			sunflareGameObject.layer = 10; //start in scaledspace

			//didn't want to serialize the matrices directly as the result is pretty unreadable
			//sorry about the mess, I'll make a cleaner way later
			//ghost 1
			Matrix4x4 ghost1Settings1 = Matrix4x4.zero;
			for (int i=0; i<ghost1SettingsList1.Count; i++)
			{
				ghost1Settings1.SetRow(i,ghost1SettingsList1[i]);
			}
			Matrix4x4 ghost1Settings2 = Matrix4x4.zero;
			for (int i=0; i<ghost1SettingsList2.Count; i++)
			{
				ghost1Settings2.SetRow(i,ghost1SettingsList2[i]);
			}

			//ghost 2
			Matrix4x4 ghost2Settings1 = Matrix4x4.zero;
			for (int i=0; i<ghost2SettingsList1.Count; i++)
			{
				ghost2Settings1.SetRow(i,ghost2SettingsList1[i]);
			}
			Matrix4x4 ghost2Settings2 = Matrix4x4.zero;
			for (int i=0; i<ghost2SettingsList2.Count; i++)
			{
				ghost2Settings2.SetRow(i,ghost2SettingsList2[i]);
			}

			//ghost 3
			Matrix4x4 ghost3Settings1 = Matrix4x4.zero;
			for (int i=0; i<ghost3SettingsList1.Count; i++)
			{
				ghost3Settings1.SetRow(i,ghost3SettingsList1[i]);
			}
			Matrix4x4 ghost3Settings2 = Matrix4x4.zero;
			for (int i=0; i<ghost3SettingsList2.Count; i++)
			{
				ghost3Settings2.SetRow(i,ghost3SettingsList2[i]);
			}

			extinctionTexture = new RenderTexture (4, 4, 0, RenderTextureFormat.ARGB32);
			extinctionTexture.antiAliasing = 1;
			extinctionTexture.filterMode = FilterMode.Point;
			extinctionTexture.Create ();

			sunglareMaterial.SetVector ("flareSettings", flareSettings);
			sunglareMaterial.SetVector ("spikesSettings", spikesSettings);

			sunglareMaterial.SetMatrix ("ghost1Settings1", ghost1Settings1);
			sunglareMaterial.SetMatrix ("ghost1Settings2", ghost1Settings2);

			sunglareMaterial.SetMatrix ("ghost2Settings1", ghost2Settings1);
			sunglareMaterial.SetMatrix ("ghost2Settings2", ghost2Settings2);

			sunglareMaterial.SetMatrix ("ghost3Settings1", ghost3Settings1);
			sunglareMaterial.SetMatrix ("ghost3Settings2", ghost3Settings2);

			sunglareMaterial.SetTexture ("extinctionTexture", extinctionTexture);


			scaledCameraHook = (SunflareCameraHook) Core.Instance.scaledSpaceCamera.gameObject.AddComponent (typeof(SunflareCameraHook));
			scaledCameraHook.flare = this;
			scaledCameraHook.useDbufferOnCamera = 0f;

			if (!(HighLogic.LoadedScene == GameScenes.TRACKSTATION))
			{
				nearCameraHook = (SunflareCameraHook)Core.Instance.nearCamera.gameObject.AddComponent (typeof(SunflareCameraHook));
				nearCameraHook.flare = this;
				nearCameraHook.useDbufferOnCamera = 1f;
			}

			Debug.Log ("[Scatterer] Added custom sun flare for "+sourceName);

		}

		public void updateProperties()
		{
			sunViewPortPos = Core.Instance.scaledSpaceCamera.WorldToViewportPoint
				(ScaledSpace.LocalToScaledSpace(source.transform.position));

			float dist = (float) (Core.Instance.scaledSpaceCamera.transform.position - ScaledSpace.LocalToScaledSpace (source.transform.position))
				.magnitude;

			sunGlareScale = dist / 2266660f * Core.Instance.scaledSpaceCamera.fieldOfView / 60f;

			//if dist > 1.25*sunglareFadeDistance -->1
			//if dist < 0.25*sunglareFadeDistance -->0
			//else values smoothstepped in between
			sunGlareFade = Mathf.SmoothStep(0,1,(dist/sunGlareFadeDistance)-0.25f);

			//if dist < 0.5 * ghostFadeDistance -->1
			//if dist > 1.5 * ghostFadeDistance -->0
			//else values smoothstepped in between
			ghostFade = Mathf.SmoothStep(0,1,(dist-0.5f*ghostFadeDistance)/(ghostFadeDistance));
			ghostFade = 1 - ghostFade;

			hitStatus=false;
			if (!MapView.MapIsEnabled && !(HighLogic.LoadedScene == GameScenes.TRACKSTATION))
			//if (!MapView.MapIsEnabled)
			{
	
				hitStatus = Physics.Raycast (Core.Instance.farCamera.transform.position,
				                             (source.transform.position- Core.Instance.farCamera.transform.position).normalized,
				                             out hit, Mathf.Infinity, (int)((1 << 15) + (1 << 0)));
				if(!hitStatus)
				{
					hitStatus = Physics.Raycast (Core.Instance.scaledSpaceCamera.transform.position,
					                             (ScaledSpace.LocalToScaledSpace(source.transform.position)- Core.Instance.scaledSpaceCamera.transform.position)
					                             .normalized,out hit, Mathf.Infinity, (int)((1 << 10)));
				}
			}
			else
			{
				hitStatus = Physics.Raycast (Core.Instance.scaledSpaceCamera.transform.position, (ScaledSpace.LocalToScaledSpace(source.transform.position)
				                                                                           - Core.Instance.transform.position).normalized,out hit, Mathf.Infinity, (int)((1 << 10)));
			}

			if(hitStatus)
			{
				//if sun visible, draw sunflare
				if(hit.transform == sourceScaledTransform)
					hitStatus=false;
			}

			eclipse = hitStatus;
			sunglareMaterial.SetFloat("renderSunFlare", (!eclipse && (sunViewPortPos.z > 0) && !Core.Instance.underwater ) ? 1.0f : 0.0f);

			sunglareMaterial.SetVector ("sunViewPortPos", sunViewPortPos);
			sunglareMaterial.SetFloat ("aspectRatio", Core.Instance.scaledSpaceCamera.aspect);
			sunglareMaterial.SetFloat ("sunGlareScale", sunGlareScale);
			sunglareMaterial.SetFloat ("sunGlareFade", sunGlareFade);
			sunglareMaterial.SetFloat ("ghostFade", ghostFade);

			if (!(HighLogic.LoadedScene == GameScenes.TRACKSTATION))
				sunglareMaterial.SetTexture ("_customDepthTexture", Core.Instance.bufferRenderingManager.depthTexture);
		}	

		public void updateNode()
		{
			//if rendertexture is lost, wait a bit before re-creating it
			if (!extinctionTexture.IsCreated())
			{
				waitBeforeReloadCnt++;
				if (waitBeforeReloadCnt >= 2)
				{
					extinctionTexture.Create();
					waitBeforeReloadCnt = 0;
				}
			}

			//enable or disable scaled or near script depending on trackstation or mapview
			if (!MapView.MapIsEnabled && !(HighLogic.LoadedScene == GameScenes.TRACKSTATION))
			{
				nearCameraHook.enabled = true;
				scaledCameraHook.enabled = false;
				sunflareGameObject.layer = 15;
			}
			else
			{
				if (nearCameraHook)
					nearCameraHook.enabled=false;

				scaledCameraHook.enabled=true;
				sunflareGameObject.layer = 10;
			}
		}

		public void clearExtinction()
		{
			RenderTexture rt=RenderTexture.active;
			RenderTexture.active= extinctionTexture;			

			GL.Clear(false,true,Color.white);

			//restore active rendertexture
			RenderTexture.active=rt;
		}	

//		public void saveToConfigNode ()
//		{
//			ConfigNode cnTemp = ConfigNode.CreateConfigFromObject (this);
//			cnTemp.Save (Core.Instance.path + "/sunflare/sunflare.cfg");
//		}

		public void cleanUp()
		{
			if (nearCameraHook)
			{
				Component.Destroy (nearCameraHook);
				UnityEngine.Object.Destroy (nearCameraHook);
			}
			if (scaledCameraHook)
			{
				Component.Destroy (scaledCameraHook);
				UnityEngine.Object.Destroy (scaledCameraHook);
			}

			if (extinctionTexture)
			{
				extinctionTexture.Release();
				UnityEngine.Object.Destroy (extinctionTexture);
			}
		}

		public void loadConfigNode ()
		{
			ConfigNode cnToLoad = new ConfigNode ();
			foreach (ConfigNode _cn in Core.Instance.sunflareConfigs)
			{
				if (_cn.TryGetNode(sourceName, ref cnToLoad))
				{
					Debug.Log("[Scatterer] Sunflare config found for "+sourceName);
					break;
				}
			}	
			ConfigNode.LoadObjectFromConfig (this, cnToLoad);
		}


	}
}