using CarCareTracker.Filter;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetCollisionRecordsByVehicleId(int vehicleId)
        {
            var result = _collisionRecordDataAccess.GetCollisionRecordsByVehicleId(vehicleId);
            bool _useDescending = _config.GetUserConfig(User).UseDescending;
            if (_useDescending)
            {
                result = result.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).ToList();
            }
            else
            {
                result = result.OrderBy(x => x.Date).ThenBy(x => x.Mileage).ToList();
            }
            return PartialView("Collision/_CollisionRecords", result);
        }
        [HttpPost]
        public IActionResult SaveCollisionRecordToVehicleId(CollisionRecordInput collisionRecord)
        {
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), collisionRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            //move files from temp.
            collisionRecord.Files = collisionRecord.Files.Select(x => { return new UploadedFiles { Name = x.Name, Location = _fileHelper.MoveFileFromTemp(x.Location, "documents/") }; }).ToList();
            if (collisionRecord.Supplies.Any())
            {
                collisionRecord.RequisitionHistory.AddRange(RequisitionSupplyRecordsByUsage(collisionRecord.Supplies, DateTime.Parse(collisionRecord.Date), collisionRecord.Description));
                if (collisionRecord.CopySuppliesAttachment)
                {
                    collisionRecord.Files.AddRange(GetSuppliesAttachments(collisionRecord.Supplies));
                }
            }
            if (collisionRecord.DeletedRequisitionHistory.Any())
            {
                _vehicleLogic.RestoreSupplyRecordsByUsage(collisionRecord.DeletedRequisitionHistory, collisionRecord.Description);
            }
            var newCollisionDate = DateTime.Parse(collisionRecord.Date);
            bool isNewCollisionRecord = collisionRecord.Id == default;
            List<int> linkedCollisionReminderIds;
            if (isNewCollisionRecord)
            {
                linkedCollisionReminderIds = collisionRecord.ReminderRecordId?.Distinct().ToList() ?? new List<int>();
                if (linkedCollisionReminderIds.Any())
                {
                    foreach (int reminderRecordId in linkedCollisionReminderIds)
                    {
                        PushbackRecurringReminderRecordWithChecks(reminderRecordId, newCollisionDate, collisionRecord.Mileage);
                    }
                }
            }
            else
            {
                var existingCollisionRecord = _collisionRecordDataAccess.GetCollisionRecordById(collisionRecord.Id);
                var storedCollisionReminderIds = existingCollisionRecord?.ReminderRecordIds ?? new List<int>();
                var newlySelectedCollisionIds = collisionRecord.ReminderRecordId ?? new List<int>();
                var newCollisionLinks = newlySelectedCollisionIds.Except(storedCollisionReminderIds).Distinct().ToList();
                foreach (int reminderRecordId in newCollisionLinks)
                {
                    PushbackRecurringReminderRecordWithChecks(reminderRecordId, newCollisionDate, collisionRecord.Mileage);
                }
                linkedCollisionReminderIds = storedCollisionReminderIds.Union(newlySelectedCollisionIds).Distinct().ToList();
                if (existingCollisionRecord is not null && existingCollisionRecord.Id != default)
                {
                    CorrectLinkedReminders(storedCollisionReminderIds, existingCollisionRecord.Date, existingCollisionRecord.Mileage, newCollisionDate, collisionRecord.Mileage);
                }
            }
            var convertedRecord = collisionRecord.ToCollisionRecord();
            convertedRecord.ReminderRecordIds = linkedCollisionReminderIds;
            var result = _collisionRecordDataAccess.SaveCollisionRecordToVehicle(convertedRecord);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromGenericRecord(convertedRecord, collisionRecord.Id == default ? "repairrecord.add" : "repairrecord.update", User.Identity?.Name ?? string.Empty));
            }
            if (convertedRecord.Id != default && collisionRecord.Id == default && _config.GetUserConfig(User).EnableAutoOdometerInsert)
            {
                _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord
                {
                    Date = DateTime.Parse(collisionRecord.Date),
                    VehicleId = collisionRecord.VehicleId,
                    Mileage = collisionRecord.Mileage,
                    Notes = $"{_translator.Translate(_config.GetUserConfig(User).UserLanguage, StaticHelper.GetAutoInsertVerbiage(ImportMode.RepairRecord, false))}: {collisionRecord.Description}",
                    Files = StaticHelper.CreateAttachmentFromRecord(ImportMode.RepairRecord, convertedRecord.Id, convertedRecord.Description)
                });
            }
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
        [HttpGet]
        public IActionResult GetAddCollisionRecordPartialView()
        {
            return PartialView("Collision/_CollisionRecordModal", new CollisionRecordInput() { ExtraFields = _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.RepairRecord).ExtraFields });
        }
        [HttpGet]
        public IActionResult GetCollisionRecordForEditById(int collisionRecordId)
        {
            var result = _collisionRecordDataAccess.GetCollisionRecordById(collisionRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), result.VehicleId, HouseholdPermission.View))
            {
                return Forbid();
            }
            //convert to Input object.
            var convertedResult = new CollisionRecordInput
            {
                Id = result.Id,
                Cost = result.Cost,
                Date = result.Date.ToShortDateString(),
                Description = result.Description,
                Mileage = result.Mileage,
                Notes = result.Notes,
                VehicleId = result.VehicleId,
                Files = result.Files,
                Tags = result.Tags,
                RequisitionHistory = result.RequisitionHistory,
                ExtraFields = StaticHelper.AddExtraFields(result.ExtraFields, _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.RepairRecord).ExtraFields)
            };
            return PartialView("Collision/_CollisionRecordModal", convertedResult);
        }
        private OperationResponse DeleteCollisionRecordWithChecks(int collisionRecordId)
        {
            var existingRecord = _collisionRecordDataAccess.GetCollisionRecordById(collisionRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Delete))
            {
                return OperationResponse.Failed("Access Denied");
            }
            //restore any requisitioned supplies.
            if (existingRecord.RequisitionHistory.Any())
            {
                _vehicleLogic.RestoreSupplyRecordsByUsage(existingRecord.RequisitionHistory, existingRecord.Description);
            }
            var result = _collisionRecordDataAccess.DeleteCollisionRecordById(existingRecord.Id);
            if (result)
            {
                RollbackLinkedReminders(existingRecord.ReminderRecordIds ?? new List<int>());
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromGenericRecord(existingRecord, "repairrecord.delete", User.Identity?.Name ?? string.Empty));
            }
            return OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage);
        }
        [HttpPost]
        public IActionResult DeleteCollisionRecordById(int collisionRecordId)
        {
            var result = DeleteCollisionRecordWithChecks(collisionRecordId);
            return Json(result);
        }
    }
}
