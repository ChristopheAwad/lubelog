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
        public IActionResult GetInspectionRecordTemplatesByVehicleId(int vehicleId)
        {
            var result = _inspectionRecordTemplateDataAccess.GetInspectionRecordTemplatesByVehicleId(vehicleId);
            return PartialView("Inspection/_InspectionRecordTemplateSelector", result);
        }
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetInspectionRecordsByVehicleId(int vehicleId)
        {
            var result = _inspectionRecordDataAccess.GetInspectionRecordsByVehicleId(vehicleId);
            bool _useDescending = _config.GetUserConfig(User).UseDescending;
            if (_useDescending)
            {
                result = result.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).ToList();
            }
            else
            {
                result = result.OrderBy(x => x.Date).ThenBy(x => x.Mileage).ToList();
            }
            return PartialView("Inspection/_InspectionRecords", result);
        }
        [HttpGet]
        public IActionResult GetAddInspectionRecordTemplatePartialView()
        {
            return PartialView("Inspection/_InspectionRecordTemplateEditModal", new InspectionRecordInput());
        }
        [HttpGet]
        public IActionResult GetEditInspectionRecordTemplatePartialView(int inspectionRecordTemplateId)
        {
            var existingRecord = _inspectionRecordTemplateDataAccess.GetInspectionRecordTemplateById(inspectionRecordTemplateId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.View))
            {
                return Forbid();
            }
            if (existingRecord.ReminderRecordId.Any())
            {
                bool reminderMissing = false;
                //check if reminder still exists and is still recurring.
                foreach (int reminderRecordId in existingRecord.ReminderRecordId)
                {
                    var existingReminder = _reminderRecordDataAccess.GetReminderRecordById(reminderRecordId);
                    if (existingReminder is null || existingReminder.Id == default || !existingReminder.IsRecurring)
                    {
                        reminderMissing = true;
                        break;
                    }
                }
                if (reminderMissing)
                {
                    //clear out reminders attached to record.
                    existingRecord.ReminderRecordId = new List<int>();
                }
            }
            return PartialView("Inspection/_InspectionRecordTemplateEditModal", existingRecord);
        }
        [HttpGet]
        public IActionResult GetAddInspectionRecordFieldPartialView()
        {
            return PartialView("Inspection/_InspectionRecordField", new InspectionRecordTemplateField());
        }
        public IActionResult GetAddInspectionRecordFieldOptionsPartialView(List<InspectionRecordTemplateFieldOption> currentOptions)
        {
            if (currentOptions != null && currentOptions.Any())
            {
                return PartialView("Inspection/_InspectionRecordFieldOptions", currentOptions);
            }
            return PartialView("Inspection/_InspectionRecordFieldOptions", new List<InspectionRecordTemplateFieldOption>());
        }
        public IActionResult GetAddInspectionRecordFieldOptionPartialView()
        {
            return PartialView("Inspection/_InspectionRecordFieldOption", new InspectionRecordTemplateFieldOption());
        }
        [HttpPost]
        public IActionResult SaveInspectionRecordTemplateToVehicleId(InspectionRecordInput inspectionRecordTemplate)
        {
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), inspectionRecordTemplate.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            var result = _inspectionRecordTemplateDataAccess.SaveInspectionReportTemplateToVehicle(inspectionRecordTemplate);
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
        private OperationResponse DeleteInspectionRecordTemplateWithChecks(int inspectionRecordTemplateId)
        {
            var existingRecord = _inspectionRecordTemplateDataAccess.GetInspectionRecordTemplateById(inspectionRecordTemplateId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Delete))
            {
                return OperationResponse.Failed("Access Denied");
            }
            var result = _inspectionRecordTemplateDataAccess.DeleteInspectionRecordTemplateById(existingRecord.Id);
            return OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage);
        }
        private OperationResponse DeleteInspectionRecordWithChecks(int inspectionRecordId)
        {
            var existingRecord = _inspectionRecordDataAccess.GetInspectionRecordById(inspectionRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Delete))
            {
                return OperationResponse.Failed("Access Denied");
            }
            var result = _inspectionRecordDataAccess.DeleteInspectionRecordById(existingRecord.Id);
            if (result)
            {
                RollbackLinkedReminders(existingRecord.ReminderRecordIds ?? new List<int>());
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromInspectionRecord(existingRecord, "inspectionrecord.delete", User.Identity?.Name ?? string.Empty));
            }
            return OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage);
        }
        [HttpPost]
        public IActionResult DeleteInspectionRecordTemplateById(int inspectionRecordTemplateId)
        {
            var result = DeleteInspectionRecordTemplateWithChecks(inspectionRecordTemplateId);
            return Json(result);
        }
        [HttpPost]
        public IActionResult DeleteInspectionRecordById(int inspectionRecordId)
        {
            var result = DeleteInspectionRecordWithChecks(inspectionRecordId);
            return Json(result);
        }
        [HttpGet]
        public IActionResult GetAddInspectionRecordPartialView(int inspectionRecordTemplateId)
        {
            var existingRecord = _inspectionRecordTemplateDataAccess.GetInspectionRecordTemplateById(inspectionRecordTemplateId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            //populate date
            existingRecord.Date = DateTime.Now.ToShortDateString();
            if (existingRecord.ReminderRecordId.Any())
            {
                bool reminderMissing = false;
                //check if reminder still exists and is still recurring.
                foreach (int reminderRecordId in existingRecord.ReminderRecordId)
                {
                    var existingReminder = _reminderRecordDataAccess.GetReminderRecordById(reminderRecordId);
                    if (existingReminder is null || existingReminder.Id == default || !existingReminder.IsRecurring)
                    {
                        reminderMissing = true;
                        break;
                    }
                }
                if (reminderMissing)
                {
                    //clear out reminders attached to record.
                    existingRecord.ReminderRecordId = new List<int>();
                }
            }
            return PartialView("Inspection/_InspectionRecordModal", existingRecord);
        }
        [HttpGet]
        public IActionResult GetViewInspectionRecordPartialView(int inspectionRecordId)
        {
            var result = _inspectionRecordDataAccess.GetInspectionRecordById(inspectionRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), result.VehicleId, HouseholdPermission.View))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            return PartialView("Inspection/_InspectionRecordViewModal", result);
        }
        [HttpPost]
        public IActionResult SaveInspectionRecordToVehicleId(InspectionRecordInput inspectionRecord)
        {
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), inspectionRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            //move files from temp.
            inspectionRecord.Files = inspectionRecord.Files.Select(x => { return new UploadedFiles { Name = x.Name, Location = _fileHelper.MoveFileFromTemp(x.Location, "documents/") }; }).ToList();
            var newInspectionDate = DateTime.Parse(inspectionRecord.Date);
            bool isNewInspectionRecord = inspectionRecord.Id == default;
            List<int> linkedInspectionReminderIds;
            if (isNewInspectionRecord)
            {
                linkedInspectionReminderIds = inspectionRecord.ReminderRecordId?.Distinct().ToList() ?? new List<int>();
                if (linkedInspectionReminderIds.Any())
                {
                    foreach (int reminderRecordId in linkedInspectionReminderIds)
                    {
                        PushbackRecurringReminderRecordWithChecks(reminderRecordId, newInspectionDate, inspectionRecord.Mileage);
                    }
                }
            }
            else
            {
                var existingInspectionRecord = _inspectionRecordDataAccess.GetInspectionRecordById(inspectionRecord.Id);
                var storedInspectionReminderIds = existingInspectionRecord?.ReminderRecordIds ?? new List<int>();
                var newlySelectedInspectionIds = inspectionRecord.ReminderRecordId ?? new List<int>();
                var newInspectionLinks = newlySelectedInspectionIds.Except(storedInspectionReminderIds).Distinct().ToList();
                foreach (int reminderRecordId in newInspectionLinks)
                {
                    PushbackRecurringReminderRecordWithChecks(reminderRecordId, newInspectionDate, inspectionRecord.Mileage);
                }
                linkedInspectionReminderIds = storedInspectionReminderIds.Union(newlySelectedInspectionIds).Distinct().ToList();
                if (existingInspectionRecord is not null && existingInspectionRecord.Id != default)
                {
                    CorrectLinkedReminders(storedInspectionReminderIds, existingInspectionRecord.Date, existingInspectionRecord.Mileage, newInspectionDate, inspectionRecord.Mileage);
                }
            }
            var convertedRecord = inspectionRecord.ToInspectionRecord();
            convertedRecord.ReminderRecordIds = linkedInspectionReminderIds;
            var result = _inspectionRecordDataAccess.SaveInspectionRecordToVehicle(convertedRecord);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromInspectionRecord(convertedRecord, isNewInspectionRecord ? "inspectionrecord.add" : "inspectionrecord.update", User.Identity?.Name ?? string.Empty));
            }
            if (convertedRecord.Id != 0)
            {
                //insert into service record
                List<UploadedFiles> newAttachments = new List<UploadedFiles>();
                newAttachments.Add(new UploadedFiles { Name = inspectionRecord.Description, Location = StaticHelper.GetRecordAttachment(ImportMode.InspectionRecord, convertedRecord.Id)});
                newAttachments.AddRange(inspectionRecord.Files);
                _serviceRecordDataAccess.SaveServiceRecordToVehicle(new ServiceRecord
                {
                    Date = DateTime.Parse(inspectionRecord.Date),
                    VehicleId = inspectionRecord.VehicleId,
                    Mileage = inspectionRecord.Mileage,
                    Description = inspectionRecord.Description,
                    Cost = inspectionRecord.Cost,
                    Notes = $"{_translator.Translate(_config.GetUserConfig(User).UserLanguage, StaticHelper.GetAutoInsertVerbiage(ImportMode.InspectionRecord, false))}: {inspectionRecord.Description}",
                    Files = newAttachments
                });
                //auto-insert into odometer if configured
                if (inspectionRecord.Id == default && _config.GetUserConfig(User).EnableAutoOdometerInsert)
                {
                    _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord
                    {
                        Date = DateTime.Parse(inspectionRecord.Date),
                        VehicleId = inspectionRecord.VehicleId,
                        Mileage = inspectionRecord.Mileage,
                        Notes = $"{_translator.Translate(_config.GetUserConfig(User).UserLanguage, StaticHelper.GetAutoInsertVerbiage(ImportMode.InspectionRecord, false))}: {inspectionRecord.Description}",
                        Files = StaticHelper.CreateAttachmentFromRecord(ImportMode.InspectionRecord, convertedRecord.Id, convertedRecord.Description)
                    });
                }
                //create action items
                var inspectionFieldsWithActionItems = inspectionRecord.Fields.Where(x => x.HasActionItem);
                if (inspectionFieldsWithActionItems.Any())
                {
                    foreach (InspectionRecordTemplateField inspectionField in inspectionFieldsWithActionItems)
                    {
                        if (inspectionField.ToInspectionRecordResult().Failed)
                        {
                            _planRecordDataAccess.SavePlanRecordToVehicle(new PlanRecord
                            {
                                DateCreated = DateTime.Now,
                                DateModified = DateTime.Now,
                                VehicleId = inspectionRecord.VehicleId,
                                Description = inspectionField.ActionItemDescription,
                                ImportMode = inspectionField.ActionItemType,
                                Priority = inspectionField.ActionItemPriority,
                                Progress = PlanProgress.Backlog,
                                Notes = $"{_translator.Translate(_config.GetUserConfig(User).UserLanguage, StaticHelper.GetAutoInsertVerbiage(ImportMode.GasRecord, false))}: {inspectionRecord.Description}",
                                Files = StaticHelper.CreateAttachmentFromRecord(ImportMode.InspectionRecord, convertedRecord.Id, convertedRecord.Description)
                            });
                        }
                    }
                }
            }
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
        [HttpPost]
        public IActionResult UpdateInspectionRecord(InspectionRecordInput inspectionRecord)
        {
            var existingRecord = _inspectionRecordDataAccess.GetInspectionRecordById(inspectionRecord.Id);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            existingRecord.Tags = inspectionRecord.Tags;
            existingRecord.Files = inspectionRecord.Files.Select(x => { return new UploadedFiles { Name = x.Name, Location = _fileHelper.MoveFileFromTemp(x.Location, "documents/") }; }).ToList();
            var result = _inspectionRecordDataAccess.SaveInspectionRecordToVehicle(existingRecord);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromInspectionRecord(existingRecord, "inspectionrecord.update", User.Identity?.Name ?? string.Empty));
            }
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
    }
}
