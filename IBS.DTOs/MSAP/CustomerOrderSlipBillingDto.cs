namespace IBS.DTOs.MSAP
{
    public class CustomerOrderSlipBillingDto
    {
        public CustomerOrderSlipHeaderDto Header { get; set; } = new();
        public List<CustomerOrderSlipTicketDto> Tickets { get; set; } = new();
    }

    public class CustomerOrderSlipHeaderDto
    {
        public int VesselId { get; set; }

        public int PortId { get; set; }

        public int TerminalId { get; set; }

        public string? VoyageNumber { get; set; }

        public string? COSNumber { get; set; }

        public bool IsVatable { get; set; }

        public bool IsVatInclusive { get; set; }

        public bool PrintWht { get; set; }
    }

    public class CustomerOrderSlipTicketDto
    {
        public int DispatchTicketId { get; set; }
        public string DispatchNo { get; set; } = string.Empty;
        public string Tugboat { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public decimal Duration { get; set; }
        public decimal DispatchRate { get; set; }
        public decimal DispatchAmount { get; set; }
        public decimal BAFRate { get; set; }
        public decimal BAFDiscount { get; set; }
        public string BAFChargeType { get; set; } = string.Empty;
        public decimal BAFAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Port { get; set; }
        public string? Terminal { get; set; }
    }
}
