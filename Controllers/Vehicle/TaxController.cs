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
        public IActionResult GetTaxRecordsByVehicleId(int vehicleId)
        {
            var result = _taxRecordDataAccess.GetTaxRecordsByVehicleId(vehicleId);
            bool _useDescending = _config.GetUserConfig(User).UseDescending;
            if (_useDescending)
            {
                result = result.OrderByDescending(x => x.Date).ToList();
            }
            else
            {
                result = result.OrderBy(x => x.Date).ToList();
            }
            return PartialView("Tax/_TaxRecords", result);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpPost]
        public IActionResult CheckRecurringTaxRecords(int vehicleId)
        {
            try
            {
                var result = _vehicleLogic.UpdateRecurringTaxes(vehicleId);
                return Json(result);
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return Json(false);
            }
        }
        [HttpPost]
        public IActionResult SaveTaxRecordToVehicleId(TaxRecordInput taxRecord)
        {
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), taxRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            //move files from temp.
            taxRecord.Files = taxRecord.Files.Select(x => { return new UploadedFiles { Name = x.Name, Location = _fileHelper.MoveFileFromTemp(x.Location, "documents/") }; }).ToList();
            var newTaxDate = DateTime.Parse(taxRecord.Date);
            bool isNewTaxRecord = taxRecord.Id == default;
            List<int> linkedTaxReminderIds;
            if (isNewTaxRecord)
            {
                linkedTaxReminderIds = taxRecord.ReminderRecordId?.Distinct().ToList() ?? new List<int>();
                if (linkedTaxReminderIds.Any())
                {
                    foreach (int reminderRecordId in linkedTaxReminderIds)
                    {
                        PushbackRecurringReminderRecordWithChecks(reminderRecordId, newTaxDate, null);
                    }
                }
            }
            else
            {
                var existingTaxRecord = _taxRecordDataAccess.GetTaxRecordById(taxRecord.Id);
                var storedTaxReminderIds = existingTaxRecord?.ReminderRecordIds ?? new List<int>();
                var newlySelectedTaxIds = taxRecord.ReminderRecordId ?? new List<int>();
                var newTaxLinks = newlySelectedTaxIds.Except(storedTaxReminderIds).Distinct().ToList();
                foreach (int reminderRecordId in newTaxLinks)
                {
                    PushbackRecurringReminderRecordWithChecks(reminderRecordId, newTaxDate, null);
                }
                linkedTaxReminderIds = storedTaxReminderIds.Union(newlySelectedTaxIds).Distinct().ToList();
                if (existingTaxRecord is not null && existingTaxRecord.Id != default)
                {
                    CorrectLinkedReminders(linkedTaxReminderIds, existingTaxRecord.Date, 0, newTaxDate, 0);
                }
            }
            var convertedTaxRecord = taxRecord.ToTaxRecord();
            convertedTaxRecord.ReminderRecordIds = linkedTaxReminderIds;
            var result = _taxRecordDataAccess.SaveTaxRecordToVehicle(convertedTaxRecord);
            _vehicleLogic.UpdateRecurringTaxes(taxRecord.VehicleId);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromTaxRecord(convertedTaxRecord, isNewTaxRecord ? "taxrecord.add" : "taxrecord.update", User.Identity?.Name ?? string.Empty));
            }
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
        [HttpGet]
        public IActionResult GetAddTaxRecordPartialView()
        {
            return PartialView("Tax/_TaxRecordModal", new TaxRecordInput() { ExtraFields = _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.TaxRecord).ExtraFields });
        }
        [HttpGet]
        public IActionResult GetTaxRecordForEditById(int taxRecordId)
        {
            var result = _taxRecordDataAccess.GetTaxRecordById(taxRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), result.VehicleId, HouseholdPermission.View))
            {
                return Forbid();
            }
            //convert to Input object.
            var convertedResult = new TaxRecordInput
            {
                Id = result.Id,
                Cost = result.Cost,
                Date = result.Date.ToShortDateString(),
                Description = result.Description,
                Notes = result.Notes,
                VehicleId = result.VehicleId,
                IsRecurring = result.IsRecurring,
                RecurringInterval = result.RecurringInterval,
                CustomMonthInterval = result.CustomMonthInterval,
                CustomMonthIntervalUnit = result.CustomMonthIntervalUnit,
                Files = result.Files,
                Tags = result.Tags,
                ExtraFields = StaticHelper.AddExtraFields(result.ExtraFields, _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.TaxRecord).ExtraFields)
            };
            return PartialView("Tax/_TaxRecordModal", convertedResult);
        }
        private OperationResponse DeleteTaxRecordWithChecks(int taxRecordId)
        {
            var existingRecord = _taxRecordDataAccess.GetTaxRecordById(taxRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Delete))
            {
                return OperationResponse.Failed("Access Denied");
            }
            var result = _taxRecordDataAccess.DeleteTaxRecordById(existingRecord.Id);
            if (result)
            {
                RollbackLinkedReminders(existingRecord.ReminderRecordIds ?? new List<int>());
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromTaxRecord(existingRecord, "taxrecord.delete", User.Identity?.Name ?? string.Empty));
            }
            return OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage);
        }
        [HttpPost]
        public IActionResult DeleteTaxRecordById(int taxRecordId)
        {
            var result = DeleteTaxRecordWithChecks(taxRecordId);
            return Json(result);
        }
    }
}
