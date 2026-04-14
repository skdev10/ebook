using System;
using System.Collections.Generic;

namespace EBookDashboard.Models.ViewModels
{
    public class UserPlansViewModel
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public DateTime? LastLoginAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<UserPlanBill> Bills { get; set; } = new List<UserPlanBill>();
        public PlanStatistics Statistics { get; set; } = new PlanStatistics();
    }

    public class UserPlanBill
    {
        public int BillId { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string Currency { get; set; } = "usd";
        public decimal TotalAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal Discount { get; set; }
        public decimal NetAmount => TotalAmount - Discount + TaxAmount;
        public string PaymentReference { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusBadgeClass => Status.ToLower() switch
        {
            "paid" => "bg-success",
            "pending" => "bg-warning",
            "cancelled" => "bg-danger",
            "refunded" => "bg-info",
            _ => "bg-secondary"
        };
        public List<PlanFeatureDetail> Features { get; set; } = new List<PlanFeatureDetail>();
        public bool IsActive => Status == "Paid" && ClosingDate >= DateTime.Now;
    }

    public class PlanFeatureDetail
    {
        public int FeatureId { get; set; }
        public string FeatureName { get; set; } = string.Empty;
        public string FeatureDescription { get; set; } = string.Empty;
        public string FeatureType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsUnlimited { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsActive => !ExpiryDate.HasValue || ExpiryDate >= DateTime.Now;
    }

    public class PlanStatistics
    {
        public int TotalBills { get; set; }
        public int ActiveBills { get; set; }
        public int TotalFeatures { get; set; }
        public int ActiveFeatures { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal MonthlySpent { get; set; }
        public string MostUsedFeature { get; set; } = string.Empty;
    }
}

