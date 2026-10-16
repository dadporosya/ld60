using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class EquipmentDNDObject : DragableObject
{
    public EquipmentControllerBase equipmentController;
    public StatUIShownBase statBase;
    private StatUIDisplayer statDisplayPfb;
    private StatUIDisplayer currentStatDisplay;

    [SerializeField] private float holdingTimeToShowStat = 0.367f;
    [SerializeField] private Vector2 statDisplayOffset = new Vector2(10f, 0f);
    private Coroutine _hoverDelayCoroutine;

    public void Init(EquipmentControllerBase equipmentControllerIn = null)
    {
        if (equipmentControllerIn) equipmentController = equipmentControllerIn;
        if (!equipmentController)
        {
            h.Out("No equipment assigned");
            return;
        }

        if (equipmentController.icon) GetComponent<Image>().sprite = equipmentController.icon;
        if (equipmentController.material) GetComponent<Image>().material = equipmentController.material;

        // h.Out("start equipment dnd");
        if (!statBase && equipmentController && equipmentController.prefabs.Count > 0)
            statBase = equipmentController.prefabs[0] as StatUIShownBase;
        if (!statBase) statBase = GetComponent<StatUIShownBase>();
        if (statBase) statBase.showByDefault = false;

        // statDisplayPfb = R.Prefabs.UI.StatDisplayerNoAvatar;
    }

    public override void Start()
    {
        base.Start();
        Init();
    }

    public void RemoveCompletely(bool destroyEquipmentInstances=true)
    {
        if (equipmentController) equipmentController.Remove(destroyEquipmentInstances);
        Destroy(gameObject);
    }

    public void Merge(
        EquipmentDNDObject other,
        bool destroyOtherOnMerge=true
    )
    {
        if (!other || !other.equipmentController) return;
        equipmentController.Merge(other.equipmentController); //main merge logic
        if (destroyOtherOnMerge)
        {
            other.RemoveCompletely(destroyEquipmentInstances: false);
        }
    }

    public override void OnHoverEnter()
    {
        base.OnHoverEnter();
        _hoverDelayCoroutine = StartCoroutine(HoverDelayRoutine());
    }

    public override void OnHoverExit()
    {
        base.OnHoverExit();
        if (_hoverDelayCoroutine != null)
        {
            StopCoroutine(_hoverDelayCoroutine);
            _hoverDelayCoroutine = null;
        }
        DestroyDisplay(currentStatDisplay);
        currentStatDisplay = null;
    }

    private IEnumerator HoverDelayRoutine()
    {
        yield return new WaitForSeconds(holdingTimeToShowStat);
        SuccessfulHover();
        _hoverDelayCoroutine = null;
    }

    private void SuccessfulHover()
    {
        if (statDisplayPfb && statBase)
        {
            if (currentStatDisplay)
            {
                DestroyDisplay(currentStatDisplay);
            }
            currentStatDisplay = Instantiate(statDisplayPfb, transform.parent);
            MoveWithTarget move = currentStatDisplay.gameObject.AddComponent<MoveWithTarget>();
            move.Init(transform);
            currentStatDisplay.UpdateAll(statBase);

            LayoutRebuilder.ForceRebuildLayoutImmediate(
                currentStatDisplay.GetComponent<RectTransform>()
            );
            Canvas.ForceUpdateCanvases();

            PositionStatDisplay();

            Vector3 initialScale = currentStatDisplay.transform.localScale;
            currentStatDisplay.transform.localScale = Vector3.zero;
            Sequence.Create()
                .Chain(Tween.Scale(
                    currentStatDisplay.transform,
                    endValue: initialScale * 1.2f,
                    duration: 0.3f,
                    ease: Ease.OutCubic
                ))
                .Chain(Tween.Scale(
                    currentStatDisplay.transform,
                    endValue: initialScale,
                    duration: 0.1f,
                    ease: Ease.InCubic
                )).OnComplete(() => { currentStatDisplay.UpdateAll(statBase); });
        }
    }

    private void DestroyDisplay(StatUIDisplayer display)
    {
        if (!display) return;
        Sequence.Create()
            .Chain(Tween.Scale(
                display.transform,
                endValue: display.transform.localScale * 1.2f,
                duration: 0.1f,
                ease: Ease.OutCubic
            ))
            .Chain(Tween.Scale(
                display.transform,
                endValue: Vector3.zero,
                duration: 0.3f,
                ease: Ease.InCubic
            )).OnComplete(() => { Destroy(display.gameObject); });
    }

    private void PositionStatDisplay()
    {
        RectTransform thisRect    = GetComponent<RectTransform>();
        RectTransform displayRect = currentStatDisplay.GetComponent<RectTransform>();
        RectTransform parentRect  = transform.parent as RectTransform;

        Canvas canvas   = GetComponentInParent<Canvas>();
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                          ? null
                          : canvas.worldCamera;

        Vector3[] worldCorners = new Vector3[4];
        thisRect.GetWorldCorners(worldCorners);

        Vector2 screenBL = RectTransformUtility.WorldToScreenPoint(uiCamera, worldCorners[0]);
        Vector2 screenTR = RectTransformUtility.WorldToScreenPoint(uiCamera, worldCorners[2]);

        float scale         = canvas.scaleFactor;
        float displayPixelW = displayRect.rect.width  * scale;
        float displayPixelH = displayRect.rect.height * scale;

        float screenX = (screenTR.x + displayPixelW <= Screen.width)
            ? screenTR.x + displayPixelW * 0.5f
            : screenBL.x - displayPixelW * 0.5f;

        float screenY     = (screenBL.y + screenTR.y) * 0.5f;
        float topOverflow = (screenY + displayPixelH * 0.5f) - Screen.height;
        if (topOverflow > 0) screenY -= topOverflow;

        // Apply offset in UI units, converted to screen pixels
        screenX += statDisplayOffset.x * scale;
        screenY += statDisplayOffset.y * scale;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, new Vector2(screenX, screenY), uiCamera, out Vector2 localPos
        );

        displayRect.pivot            = new Vector2(0.5f, 0.5f);
        displayRect.anchorMin        = new Vector2(0.5f, 0.5f);
        displayRect.anchorMax        = new Vector2(0.5f, 0.5f);
        displayRect.anchoredPosition = localPos;
    }

    private void OnDestroy()
    {
        DestroyDisplay(currentStatDisplay);
    }
}