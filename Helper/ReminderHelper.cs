using CarCareTracker.Models;

namespace CarCareTracker.Helper
{
    public interface IReminderHelper
    {
        ReminderRecord GetUpdatedRecurringReminderRecord(ReminderRecord existingReminder, DateTime? currentDate, int? currentMileage);
        ReminderRecord GetCorrectedRecurringReminderRecord(ReminderRecord existingReminder, DateTime oldDate, int oldMileage, DateTime newDate, int newMileage);
        ReminderRecord GetRolledBackRecurringReminderRecord(ReminderRecord existingReminder);
        List<ReminderRecordViewModel> GetReminderRecordViewModels(List<ReminderRecord> reminders, int currentMileage, DateTime dateCompare);
    }
    public class ReminderHelper: IReminderHelper
    {
        private readonly IConfigHelper _config;
        public ReminderHelper(IConfigHelper config)
        {
            _config = config;
        }
        public ReminderRecord GetUpdatedRecurringReminderRecord(ReminderRecord existingReminder, DateTime? currentDate, int? currentMileage)
        {
            var newDate = existingReminder.FixedIntervals ? existingReminder.Date : currentDate ?? existingReminder.Date;
            var newMileage = existingReminder.FixedIntervals ? existingReminder.Mileage : currentMileage ?? existingReminder.Mileage;
            if (existingReminder.Metric == ReminderMetric.Both)
            {
                if (existingReminder.ReminderMonthInterval != ReminderMonthInterval.Other)
                {
                    existingReminder.Date = newDate.AddMonths((int)existingReminder.ReminderMonthInterval);
                } else
                {
                    if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Months)
                    {
                        existingReminder.Date = newDate.Date.AddMonths(existingReminder.CustomMonthInterval);
                    } 
                    else if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Days)
                    {
                        existingReminder.Date = newDate.Date.AddDays(existingReminder.CustomMonthInterval);
                    }
                }
               
                if (existingReminder.ReminderMileageInterval != ReminderMileageInterval.Other)
                {
                    existingReminder.Mileage = newMileage + (int)existingReminder.ReminderMileageInterval;
                }
                else
                {
                    existingReminder.Mileage = newMileage + existingReminder.CustomMileageInterval;
                }
            }
            else if (existingReminder.Metric == ReminderMetric.Odometer)
            {
                if (existingReminder.ReminderMileageInterval != ReminderMileageInterval.Other)
                {
                    existingReminder.Mileage = newMileage + (int)existingReminder.ReminderMileageInterval;
                } else
                {
                    existingReminder.Mileage = newMileage + existingReminder.CustomMileageInterval;
                }
            }
            else if (existingReminder.Metric == ReminderMetric.Date)
            {
                if (existingReminder.ReminderMonthInterval != ReminderMonthInterval.Other)
                {
                    existingReminder.Date = newDate.AddMonths((int)existingReminder.ReminderMonthInterval);
                }
                else
                {
                    if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Months)
                    {
                        existingReminder.Date = newDate.AddMonths(existingReminder.CustomMonthInterval);
                    }
                    else if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Days)
                    {
                        existingReminder.Date = newDate.AddDays(existingReminder.CustomMonthInterval);
                    }
                }
            }
            return existingReminder;
        }
        public ReminderRecord GetCorrectedRecurringReminderRecord(ReminderRecord existingReminder, DateTime oldDate, int oldMileage, DateTime newDate, int newMileage)
        {
            //Adjust a reminder that was previously pushed forward by a service/repair record
            //when that record's date/mileage is edited. Uses delta so manual edits made
            //after the push are preserved. Fixed-interval reminders ignore service values.
            if (existingReminder.FixedIntervals)
            {
                return existingReminder;
            }
            bool touchesDate = existingReminder.Metric == ReminderMetric.Date || existingReminder.Metric == ReminderMetric.Both;
            bool touchesMileage = existingReminder.Metric == ReminderMetric.Odometer || existingReminder.Metric == ReminderMetric.Both;
            if (touchesDate && oldDate.Date != newDate.Date)
            {
                var dateDelta = (newDate.Date - oldDate.Date).Days;
                existingReminder.Date = existingReminder.Date.AddDays(dateDelta);
            }
            if (touchesMileage && oldMileage != newMileage)
            {
                existingReminder.Mileage = existingReminder.Mileage + (newMileage - oldMileage);
            }
            return existingReminder;
        }
        public ReminderRecord GetRolledBackRecurringReminderRecord(ReminderRecord existingReminder)
        {
            //Undo a single push-forward (used when the linked service/repair record is deleted).
            if (existingReminder.Metric == ReminderMetric.Both || existingReminder.Metric == ReminderMetric.Date)
            {
                if (existingReminder.ReminderMonthInterval != ReminderMonthInterval.Other)
                {
                    existingReminder.Date = existingReminder.Date.AddMonths(-(int)existingReminder.ReminderMonthInterval);
                }
                else
                {
                    if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Months)
                    {
                        existingReminder.Date = existingReminder.Date.AddMonths(-existingReminder.CustomMonthInterval);
                    }
                    else if (existingReminder.CustomMonthIntervalUnit == ReminderIntervalUnit.Days)
                    {
                        existingReminder.Date = existingReminder.Date.AddDays(-existingReminder.CustomMonthInterval);
                    }
                }
            }
            if (existingReminder.Metric == ReminderMetric.Both || existingReminder.Metric == ReminderMetric.Odometer)
            {
                int mileageInterval = existingReminder.ReminderMileageInterval != ReminderMileageInterval.Other
                    ? (int)existingReminder.ReminderMileageInterval
                    : existingReminder.CustomMileageInterval;
                existingReminder.Mileage = existingReminder.Mileage - mileageInterval;
            }
            return existingReminder;
        }
        public List<ReminderRecordViewModel> GetReminderRecordViewModels(List<ReminderRecord> reminders, int currentMileage, DateTime dateCompare)
        {
            List<ReminderRecordViewModel> reminderViewModels = new List<ReminderRecordViewModel>();
            var reminderUrgencyConfig = _config.GetReminderUrgencyConfig();
            foreach (var reminder in reminders)
            {
                if (reminder.UseCustomThresholds)
                {
                    reminderUrgencyConfig = reminder.CustomThresholds;
                }
                var reminderViewModel = new ReminderRecordViewModel()
                {
                    Id = reminder.Id,
                    VehicleId = reminder.VehicleId,
                    Date = reminder.Date,
                    Mileage = reminder.Mileage,
                    Description = reminder.Description,
                    Notes = reminder.Notes,
                    Metric = reminder.Metric,
                    UserMetric = reminder.Metric,
                    IsRecurring = reminder.IsRecurring,
                    Tags = reminder.Tags
                };
                if (reminder.Metric == ReminderMetric.Both)
                {
                    if (reminder.Date < dateCompare)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.PastDue;
                        reminderViewModel.Metric = ReminderMetric.Date;
                    }
                    else if (reminder.Mileage < currentMileage)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.PastDue;
                        reminderViewModel.Metric = ReminderMetric.Odometer;
                    }
                    else if (reminder.Date < dateCompare.AddDays(reminderUrgencyConfig.VeryUrgentDays))
                    {
                        //if less than a week from today or less than 50 miles from current mileage then very urgent.
                        reminderViewModel.Urgency = ReminderUrgency.VeryUrgent;
                        //have to specify by which metric this reminder is urgent.
                        reminderViewModel.Metric = ReminderMetric.Date;
                    }
                    else if (reminder.Mileage < currentMileage + reminderUrgencyConfig.VeryUrgentDistance)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.VeryUrgent;
                        reminderViewModel.Metric = ReminderMetric.Odometer;
                    }
                    else if (reminder.Date < dateCompare.AddDays(reminderUrgencyConfig.UrgentDays))
                    {
                        reminderViewModel.Urgency = ReminderUrgency.Urgent;
                        reminderViewModel.Metric = ReminderMetric.Date;
                    }
                    else if (reminder.Mileage < currentMileage + reminderUrgencyConfig.UrgentDistance)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.Urgent;
                        reminderViewModel.Metric = ReminderMetric.Odometer;
                    }
                    reminderViewModel.DueDays = (reminder.Date - dateCompare).Days;
                    reminderViewModel.DueMileage = reminder.Mileage - currentMileage;
                }
                else if (reminder.Metric == ReminderMetric.Date)
                {
                    if (reminder.Date < dateCompare)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.PastDue;
                    }
                    else if (reminder.Date < dateCompare.AddDays(reminderUrgencyConfig.VeryUrgentDays))
                    {
                        reminderViewModel.Urgency = ReminderUrgency.VeryUrgent;
                    }
                    else if (reminder.Date < dateCompare.AddDays(reminderUrgencyConfig.UrgentDays))
                    {
                        reminderViewModel.Urgency = ReminderUrgency.Urgent;
                    }
                    reminderViewModel.DueDays = (reminder.Date - dateCompare).Days;
                }
                else if (reminder.Metric == ReminderMetric.Odometer)
                {
                    if (reminder.Mileage < currentMileage)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.PastDue;
                        reminderViewModel.Metric = ReminderMetric.Odometer;
                    }
                    else if (reminder.Mileage < currentMileage + reminderUrgencyConfig.VeryUrgentDistance)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.VeryUrgent;
                    }
                    else if (reminder.Mileage < currentMileage + reminderUrgencyConfig.UrgentDistance)
                    {
                        reminderViewModel.Urgency = ReminderUrgency.Urgent;
                    }
                    reminderViewModel.DueMileage = reminder.Mileage - currentMileage;
                }
                reminderViewModels.Add(reminderViewModel);
            }
            return reminderViewModels;
        }
    }
}
