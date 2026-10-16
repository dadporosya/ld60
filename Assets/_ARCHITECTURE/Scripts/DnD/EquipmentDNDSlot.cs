using System;
using UnityEngine;

public class EquipmentDNDSlot : SlotBase
{
    public Unit unit;
    public EquipmentControllerBase currentEquipmentController;
    
    public P.EquipmentType equipmentType = P.EquipmentType.Any;
    public override void Awake()
    {
         base.Awake();
         
         OnPlacedObjectEvent.AddListener(ApplyEquipment);
         OnRemovedObjectEvent.AddListener(RemoveEquipment);

         placingType = PlacingType.Swap;
    }

    public void Init(Unit unitIn)
    {
        unit = unitIn;
    }

    public override bool PlacingCondition(DragableObject draggingObject=null)
    {
        if (!base.PlacingCondition())
        {
            return false;
        }

        if (!draggingObject) return false;
        
        // basic condition
        EquipmentDNDObject equipDND = draggingObject.GetComponent<EquipmentDNDObject>();
        EquipmentControllerBase equipmentController = equipDND.equipmentController;
        if (!equipDND || !equipmentController) return false;
        
        if (equipmentController.type != equipmentType && equipmentType != P.EquipmentType.Any)
        {
            return false;
        }
        
        // unit condition
        if (!unit) return false;
        if (equipmentController.type != P.EquipmentType.Any
            && unit.equipmentSlotsCurrentCount[equipmentController.type] >= unit.equipmentSlotsMaxCount[equipmentController.type]
            ) // todo
        {
            h.Out("Max equipment slot count reached");
            return false;
        }        
        
        //swap condition
        if (currentEquipmentController
            && equipmentController
            && (
                currentEquipmentController.type != equipmentController.type
                || currentEquipmentController.type != P.EquipmentType.Any
                || equipmentController.type != P.EquipmentType.Any
            )
           ) return false;
        
        return true;
    }

    public void ApplyEquipment()
    {
        if (currentEquipmentController && unit)
        {
            unit.RemoveEquipment(currentEquipmentController, true);
        }
        
        currentEquipmentController = containingObject.GetComponent<EquipmentDNDObject>().equipmentController;
        if (!currentEquipmentController)
        {
            h.Out("no equipment found");
            return;
        }

        if (!unit)
        {
            h.Out("no unit");
            return;
        }
        currentEquipmentController.Init(unit.gameObject);
    }

    public void RemoveEquipment()
    {
        if (currentEquipmentController)
        {
            currentEquipmentController.Remove();
            currentEquipmentController = null;
        }
    }
}
