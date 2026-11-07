using NUnit.Framework;
using RoomSiege.Enemies;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class ModelVisualTests
    {
        private GameObject root;
        private Material material;
        [SetUp]public void Setup(){root=new GameObject("Visual test");material=new Material(Resources.Load<Shader>("RoomSiegeEnemySilhouette"));}
        [TearDown]public void Cleanup(){Object.DestroyImmediate(root);Object.DestroyImmediate(material);}
        private GruntModel Grunt()
        {var go=new GameObject("Grunt");go.transform.SetParent(root.transform);var model=go.AddComponent<GruntModel>();model.Build(material);return model;}

        [Test]public void TrapExplosionPoolIsBoundedPausesAndClearsWithoutColliders()
        {
            var pool=root.AddComponent<TrapExplosionVisuals>();pool.Build(material);
            pool.Emit(new Vector3(2,.42f,3),3);
            Assert.Less(Vector3.Distance(new Vector3(2,.455f,3),root.transform.Find("Trap explosion 0").position),.0001f);
            var shock=root.transform.Find("Trap explosion 0/Expanding shock ring");
            float initial=shock.localScale.x;
            pool.Tick(.2f,true);Assert.AreEqual(initial,shock.localScale.x);
            pool.Tick(.2f,false);Assert.Greater(shock.localScale.x,initial);
            pool.Emit(new Vector3(float.NaN,0,0),1);Assert.AreEqual(1,pool.VisibleCount);
            for(int i=0;i<40;i++)pool.Emit(Vector3.zero,1);
            Assert.AreEqual(TrapExplosionVisuals.Capacity,pool.VisibleCount);
            Assert.AreEqual(TrapExplosionVisuals.Capacity,root.transform.childCount);
            Assert.AreEqual(0,root.GetComponentsInChildren<Collider>(true).Length);
            pool.Tick(TrapExplosionVisuals.Duration,false);Assert.AreEqual(0,pool.VisibleCount);
            pool.Emit(Vector3.zero,1);pool.Clear();Assert.AreEqual(0,pool.VisibleCount);
        }

        [Test]public void TrapExplosionRendersVisibleFramesAndFadesOut()
        {
            var blastMaterial=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            var cameraObject=new GameObject("Explosion capture camera");
            var target=new RenderTexture(640,480,24);
            var pixels=new Texture2D(640,480,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                var pool=root.AddComponent<TrapExplosionVisuals>();pool.Build(blastMaterial);
                foreach(var item in root.GetComponentsInChildren<Transform>(true))item.gameObject.layer=30;
                var camera=cameraObject.AddComponent<Camera>();camera.cullingMask=1<<30;
                camera.transform.position=new Vector3(1.1f,1.15f,-1.35f);camera.transform.LookAt(new Vector3(0,.12f,0));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.fieldOfView=45;
                camera.targetTexture=target;
                System.IO.Directory.CreateDirectory("TestResults/TrapExplosion");
                pool.Emit(Vector3.zero,3);
                float[] steps={.04f,.16f,.27f,.25f};
                for(int frame=0;frame<steps.Length;frame++)
                {
                    pool.Tick(steps[frame],false);camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();
                    int lit=0;foreach(var pixel in pixels.GetPixels32())if(pixel.r+pixel.g+pixel.b>45)lit++;
                    if(frame<3)Assert.Greater(lit,100,"Explosion must be visibly rendered at frame "+frame);
                    else Assert.AreEqual(0,lit,"Completed explosion leaves no lingering glow");
                    System.IO.File.WriteAllBytes("TestResults/TrapExplosion/frame-"+frame+".png",pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active=previous;
                Object.DestroyImmediate(cameraObject);target.Release();Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);Object.DestroyImmediate(blastMaterial);
            }
        }

        [Test]public void GruntIsOneMeshInsideExistingBodyEnvelopeWithNoCosmeticCollision()
        {
            var model=Grunt();Assert.AreEqual(1,model.GetComponentsInChildren<Renderer>().Length);
            Assert.AreEqual(0,model.GetComponentsInChildren<Collider>().Length);
            Assert.Greater(model.PoseMesh.vertexCount,200);
            Assert.LessOrEqual(model.PoseMesh.bounds.max.y,.34f);
            Assert.LessOrEqual(model.PoseMesh.bounds.extents.x,.12f);
            foreach(var n in model.PoseMesh.normals)Assert.AreEqual(1,n.magnitude,.001f);
        }
        [Test]public void WalkUsesTravelDistanceAndPauseDoesNotAdvanceGait()
        {
            var model=Grunt();model.Tick(.1f,.05f);float phase=model.WalkPhase;
            Assert.Greater(phase,0);model.Tick(0,1);Assert.AreEqual(phase,model.WalkPhase);
            model.Tick(1,0);Assert.AreEqual(phase,model.WalkPhase,"Frozen and blocked enemies cannot keep walking");
            model.ResetPose();model.Tick(1,.05f);Assert.AreEqual(phase,model.WalkPhase,.0001f,"Cadence follows distance including slows");
            model.ResetPose();Assert.AreEqual(0,model.WalkPhase);
        }
        [TestCase(30)][TestCase(60)][TestCase(90)]
        public void CrowdAvoidanceSlidesWithoutExceedingSpeedBudget(int fps)
        {
            Vector3 current=Vector3.zero,neighbor=Vector3.forward*.15f;
            float step=.5f/fps;
            var next=EnemySteering.AvoidNeighbor(current,current+Vector3.forward*step,neighbor,.3f,1,2);
            Assert.Greater(next.z,0,"A crowded enemy still progresses toward its route");
            Assert.Greater(Mathf.Abs(next.x),0,"It steps sideways instead of marching in place");
            Assert.LessOrEqual(next.magnitude,step+.000001f,"Separation cannot accelerate movement");
            Assert.AreEqual(current,EnemySteering.AvoidNeighbor(current,current,neighbor,.3f,1,2),"Frozen actors cannot be pushed");
            var far=current+Vector3.forward*step;
            Assert.AreEqual(far,EnemySteering.AvoidNeighbor(current,far,Vector3.forward*2,.3f,1,2));
        }
        [Test]public void MachineGunUpgradesSkinAndFireRemainVisualOnly()
        {
            var swivel=new GameObject("Turret");swivel.transform.SetParent(root.transform,false);
            var model=swivel.AddComponent<MachineGunModel>();model.Build(root.transform,material);
            Assert.IsFalse(swivel.transform.Find("Recoil carriage/MG Lv2 targeting").GetComponent<Renderer>().enabled);
            model.SetAppearance(3,Color.yellow);Assert.AreEqual(3,model.DisplayLevel);
            Assert.IsTrue(swivel.transform.Find("Recoil carriage/MG Lv3 cooling rails").GetComponent<Renderer>().enabled);
            var block=new MaterialPropertyBlock();root.transform.Find("MG pedestal").GetComponent<Renderer>().GetPropertyBlock(block);
            Assert.AreEqual(Color.yellow,block.GetColor("_RimColor"));
            model.Fire();Assert.Greater(model.Recoil,0);float recoil=model.Recoil;model.Tick(0);Assert.AreEqual(recoil,model.Recoil);
            Assert.IsTrue(swivel.transform.Find("Recoil carriage/MG muzzle flash").GetComponent<Renderer>().enabled);
            model.Tick(1);Assert.AreEqual(0,model.Recoil);Assert.IsFalse(swivel.transform.Find("Recoil carriage/MG muzzle flash").GetComponent<Renderer>().enabled);
            Assert.AreEqual(0,root.GetComponentsInChildren<Collider>().Length);
            Assert.AreEqual(.191f,model.MuzzlePosition.z,.0001f);
        }
        [TestCase("Frost")][TestCase("Laser")][TestCase("Trap")]
        public void HolographicTowerLevelsHaveDistinctMeshesAndNoCosmeticColliders(string id)
        {
            var model=root.AddComponent<HoloTowerModel>();model.Build(id,material);
            var renderers=root.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(3,renderers.Length);
            int previous=0;
            for(int level=1;level<=3;level++)
            {
                model.SetAppearance(level,Color.cyan);int visible=0;
                foreach(var renderer in renderers)if(renderer.enabled)
                {
                    visible++;
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.Greater(mesh.vertexCount,previous,id+" upgrade changes silhouette");previous=mesh.vertexCount;
                    Assert.LessOrEqual(mesh.bounds.size.x,.23f);
                    Assert.LessOrEqual(mesh.bounds.size.z,.24f);
                }
                Assert.AreEqual(1,visible,"Only current level is rendered");
                Assert.AreEqual(level,model.DisplayLevel);
            }
            Assert.AreEqual(0,root.GetComponentsInChildren<Collider>().Length);
        }
        [Test]public void DeathSnapshotsAreBoundedIndependentPausedAndCleared()
        {
            var model=Grunt();var pool=root.AddComponent<GruntDeathVisuals>();pool.Build(material);
            pool.Emit(model);var first=root.transform.Find("Grunt dissolve 0").GetComponent<MeshFilter>().sharedMesh;
            var before=first.vertices;model.Tick(.2f,.1f);
            CollectionAssert.AreEqual(before,first.vertices,"Actor pose reuse cannot change a detached death snapshot");
            for(int i=0;i<40;i++)pool.Emit(model);
            Assert.AreEqual(GruntDeathVisuals.Capacity,pool.VisibleCount);
            pool.Tick(1,true);Assert.AreEqual(GruntDeathVisuals.Capacity,pool.VisibleCount);
            pool.Tick(1,false);Assert.AreEqual(0,pool.VisibleCount);
            pool.Emit(model);pool.Clear();Assert.AreEqual(0,pool.VisibleCount);
            model.gameObject.SetActive(false);pool.Emit(model);Assert.AreEqual(0,pool.VisibleCount);
        }
        [Test]public void EnemyPoolCanChangeSpeciesWithoutStaleModelOrAnimation()
        {
            var visual=root.AddComponent<EnemyVisual>();visual.Build(material,material);
            visual.Configure("Grunt",.34f,.12f,false);Assert.IsTrue(visual.Grunt.gameObject.activeSelf);
            Assert.IsFalse(root.transform.Find("Enemy silhouette").gameObject.activeSelf);
            visual.Tick(.2f,false,0,.1f);Assert.Greater(visual.Grunt.WalkPhase,0);
            visual.Hide();Assert.IsFalse(visual.Grunt.gameObject.activeSelf);
            visual.Configure("Shielded",.34f,.12f,true);Assert.IsFalse(visual.Grunt.gameObject.activeSelf);
            Assert.IsTrue(root.transform.Find("Enemy silhouette").gameObject.activeSelf);
            Assert.IsTrue(root.transform.Find("Shield silhouette").gameObject.activeSelf);
            visual.Configure("Grunt",.34f,.12f,false);Assert.AreEqual(0,visual.Grunt.WalkPhase);
            Assert.IsFalse(root.transform.Find("Shield silhouette").gameObject.activeSelf);
        }
        [Test]public void RepeatedWaveFourAndSevenSpeciesRestoreVisibleBodiesAfterPoolReuse()
        {
            var visual=root.AddComponent<EnemyVisual>();visual.Build(material,material);
            for(int reuse=0;reuse<8;reuse++)foreach(string id in new[]{"Runner","Grunt","Shielded","Runner","Grunt","Boss","Grunt"})
            {
                root.SetActive(false);visual.Hide();
                visual.Configure(id,id=="Boss"?.72f:id=="Runner"?.28f:.34f,id=="Boss"?.28f:id=="Runner"?.09f:.12f,id=="Shielded");
                root.SetActive(true);
                Assert.IsTrue(visual.BodyReady,id+" visible before the first combat tick after reuse "+reuse);
                visual.Tick(1/60f,id=="Shielded",0,.003f);
                Assert.IsTrue(visual.BodyReady,id+" visible after animation");
            }
        }
        [Test]public void EveryEnemyTypeHasDistinctProceduralModelAndNoCapsuleFallback()
        {
            var visual=root.AddComponent<EnemyVisual>();visual.Build(material,material);
            var ids=new[]{"Grunt","Runner","Tank","Shielded","Boss"};var poses=new Vector3[ids.Length][];
            foreach(var id in ids)
            {
                float expectedHeight=id=="Boss"?.72f:id=="Tank"?.52f:id=="Runner"?.28f:.34f;
                float expectedRadius=id=="Boss"?.28f:id=="Tank"?.20f:id=="Runner"?.09f:.12f;
                visual.Configure(id,expectedHeight,expectedRadius,id=="Shielded"||id=="Boss");
                Assert.IsFalse(root.GetComponentInChildren<CapsuleCollider>()!=null,"No enemy visual may instantiate a capsule collider");
                Assert.IsFalse(root.transform.Find("Enemy silhouette/Capsule")!=null,"No capsule mesh fallback");
                if(id!="Grunt")
                {
                    var model=root.GetComponentInChildren<EnemyTypeModel>();
                    Assert.AreEqual(0,model.WorldBottom,.002f,$"{id} feet must touch the actor origin");
                    Assert.AreEqual(expectedHeight,model.WorldHeight,.003f,$"{id} must fit its configured height");
                }
                var mesh=id=="Grunt"?visual.Grunt.PoseMesh:root.transform.Find("Enemy silhouette/Enemy silhouette").GetComponent<MeshFilter>().sharedMesh;
                poses[System.Array.IndexOf(ids,id)]=mesh.vertices;
            }
            for(int i=0;i<ids.Length;i++)
            {
                Assert.Greater(poses[i].Length,200,ids[i]+" must have a detailed body mesh");
                for(int j=0;j<i;j++)CollectionAssert.AreNotEqual(poses[i],poses[j],ids[i]+" and "+ids[j]+" need distinct geometry");
            }
            visual.Configure("Boss",.72f,.28f,true);Assert.IsTrue(root.transform.Find("Enemy silhouette/Enemy silhouette").gameObject.activeInHierarchy);
            var surface=root.GetComponentInChildren<EnemyTypeModel>().Renderer;
            visual.Hide();Assert.IsFalse(surface.enabled);Assert.IsFalse(surface.gameObject.activeInHierarchy);
        }
        [Test]public void EnemyFeetStayOnRaisedFloorAcrossWalkingAndPoolReuse()
        {
            root.transform.position=new Vector3(3,.42f,-2);root.transform.rotation=Quaternion.Euler(0,37,0);
            var visual=root.AddComponent<EnemyVisual>();visual.Build(material,material);
            var ids=new[]{"Boss","Runner","Shielded","Tank","Grunt","Boss"};
            foreach(string id in ids)
            {
                float height=id=="Boss"?.72f:id=="Tank"?.52f:id=="Runner"?.28f:.34f;
                float radius=id=="Boss"?.28f:id=="Tank"?.20f:id=="Runner"?.09f:.12f;
                visual.Configure(id,height,radius,id=="Shielded"||id=="Boss");
                if(id=="Grunt"){visual.Tick(.1f,false,0,.02f);visual.Hide();continue;}
                var model=root.GetComponentInChildren<EnemyTypeModel>();Vector3 scale=model.transform.localScale;
                Assert.AreEqual(.42f,model.WorldBottom,.002f,id+" rest foot contact");
                Assert.AreEqual(height,model.WorldHeight,.003f,id+" rest size");
                var mesh=model.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.LessOrEqual(Mathf.Max(mesh.bounds.size.x*scale.x,mesh.bounds.size.z*scale.z),radius*2+.001f,id+" rest footprint");
                Assert.AreEqual(height*.5f,root.transform.Find("Shield silhouette").localPosition.y,.0001f);
                for(int frame=0;frame<180;frame++)
                {
                    root.transform.position+=new Vector3(.002f,0,.001f);
                    visual.Tick(1/60f,id=="Shielded"||id=="Boss",0,.004f);
                    Assert.AreEqual(.42f,model.WorldBottom,.002f,id+" animated foot contact at "+frame);
                    Assert.AreEqual(scale,model.transform.localScale,id+" animation must not resize the model");
                    Assert.That(model.WorldHeight,Is.InRange(height*.94f,height*1.06f),id+" walking height");
                }
                visual.Hide();Assert.IsFalse(model.Renderer.enabled);Assert.IsFalse(model.gameObject.activeInHierarchy);
            }
        }
    }
}
