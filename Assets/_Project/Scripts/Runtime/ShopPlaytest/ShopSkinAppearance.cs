// 职责：复用场景现成小人换外观；Taming 控制活体，不适合搬动尸体，通用小人也不应认识换皮规则。
using System;
using System.Collections.Generic;
using Game.CharacterPuppet;
using Game.Monster;
using UnityEngine;
namespace Game.ShopPlaytest
{
    public sealed class ShopSkinAppearance : IDisposable
    {
        private readonly EncounterSceneView view;
        private readonly ChibiPuppet original;
        private readonly Dictionary<string, ChibiPuppet> skins = new Dictionary<string, ChibiPuppet>();
        private ChibiPuppet worn;
        private ChibiPuppetMotion motion;
        private Transform parent, trackedRoot;
        private SpriteRenderer facing;
        private Vector3 position, scale;
        private Quaternion rotation;
        public ShopSkinAppearance(EncounterSceneView view, IReadOnlyList<ShopPlaytestActor> actors)
        {
            this.view = view;
            original = view.PlayerBody.GetComponentInChildren<ChibiPuppet>(true);
            foreach (ShopPlaytestActor actor in actors)
                if (actor != null && ShopSkinRules.ItemId(actor.Role) != 0)
                    skins[actor.StableId] = actor.GetComponentInChildren<ChibiPuppet>(true);
        }
        public bool CanWear(string id) => original != null && skins.TryGetValue(id, out ChibiPuppet skin) && skin != null;
        public void Wear(string id)
        {
            Clear();
            worn = skins[id];
            Transform root = worn.transform;
            parent = root.parent; position = root.localPosition; rotation = root.localRotation; scale = root.localScale;
            motion = worn.GetComponent<ChibiPuppetMotion>();
            if (motion != null) { trackedRoot = motion.TrackedRoot; facing = motion.FacingSource; }
            // 九种现成皮都直接挂角色根；保留作者姿态，不能套玩家纸片的俯角，否则拆件脚部会穿地。
            root.SetParent(view.PlayerBody, false);
            root.localPosition = position;
            root.localRotation = rotation;
            if (motion != null) motion.BindMotion(view.PlayerBody, view.PlayerIdentity.Visual);
            original.gameObject.SetActive(false);
            foreach (Renderer renderer in worn.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            worn.gameObject.SetActive(true);
        }
        public void Clear()
        {
            if (worn == null) return;
            Transform root = worn.transform;
            root.SetParent(parent, false); root.localPosition = position; root.localRotation = rotation; root.localScale = scale;
            if (motion != null && trackedRoot != null) motion.BindMotion(trackedRoot, facing);
            if (original != null) original.gameObject.SetActive(true);
            worn.gameObject.SetActive(false);
            worn = null; motion = null;
        }
        public void Dispose() => Clear();
    }
}
