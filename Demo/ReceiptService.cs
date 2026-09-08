using Demo.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net.Mail;

namespace Demo;

public class ReceiptService(DB db, Helper hp)
{
    public Order? GetOrderForReceipt(int orderId)
    {
        return db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.SelectedModifiers)
            .Include(o => o.Voucher)
            .Include(o => o.User)
            .FirstOrDefault(o => o.Id == orderId);
    }

    public byte[] GenerateReceiptPdf(Order order)
    {
        var customerName = order.User?.Name ?? order.GuestName ?? "Guest";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A5);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Receipt").FontSize(18).Bold();
                    col.Item().Text($"Order #{order.Id}");
                    col.Item().Text($"{order.CreatedAt:dd MMM yyyy, hh:mm tt}");
                    col.Item().Text($"Customer: {customerName}");
                    col.Item().Text($"Payment Method: {order.PaymentMethod}");
                });

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Text("Item").Bold();
                        h.Cell().Text("Qty").Bold();
                        h.Cell().Text("Price").Bold();
                        h.Cell().Text("Total").Bold();
                    });

                    foreach (var item in order.OrderItems)
                    {
                        table.Cell().Text(item.ProductNameSnapshot);
                        table.Cell().Text(item.Quantity.ToString());
                        table.Cell().Text($"RM {item.UnitPriceSnapshot:0.00}");
                        table.Cell().Text($"RM {item.LineTotal:0.00}");

                        foreach (var mod in item.SelectedModifiers)
                        {
                            table.Cell().Text($"  + {mod.ModifierOptionNameSnapshot}").FontSize(8);
                            table.Cell();
                            table.Cell().Text($"RM {mod.ExtraPriceSnapshot:0.00}").FontSize(8);
                            table.Cell();
                        }
                    }
                });

                page.Footer().Column(col =>
                {
                    var sst = order.Total - order.Subtotal + order.DiscountAmount;

                    col.Item().Text($"Subtotal: RM {order.Subtotal:0.00}");
                    col.Item().Text($"SST: RM {sst:0.00}");
                    if (order.DiscountAmount > 0)
                        col.Item().Text($"Discount ({order.Voucher?.Code}): -RM {order.DiscountAmount:0.00}");
                    col.Item().Text($"Total: RM {order.Total:0.00}").Bold();
                });
            });
        });

        return doc.GeneratePdf();
    }

    // email is passed in explicitly — members: order.User.Email;
    // guests: pulled from IMemoryCache by the caller (see PaymentController).
    public void SendReceiptEmail(Order order, string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var pdfBytes = GenerateReceiptPdf(order);

        using var mail = new MailMessage();
        mail.To.Add(email);
        mail.Subject = $"Your Receipt - Order #{order.Id}";
        mail.Body = $"Hi {order.User?.Name ?? order.GuestName}, thanks for your order! Your receipt is attached.";
        mail.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), $"Receipt_Order{order.Id}.pdf", "application/pdf"));

        hp.SendEmail(mail);
    }
}