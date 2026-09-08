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
        var sst = order.Total - order.Subtotal + order.DiscountAmount;

        var primaryColor = "#0D6EFD";
        var darkText = "#1A1A2E";
        var mutedText = "#8A8FA3";
        var borderColor = "#EDF0F5";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(0);
                page.Size(PageSizes.A5);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(darkText));

                page.Content().Column(col =>
                {
                    // ---- Header band ----
                    col.Item().Background(primaryColor).Padding(24).Column(header =>
                    {
                        header.Item().Text("RECEIPT").FontSize(22).Bold().FontColor(Colors.White);
                        header.Item().PaddingTop(4).Text($"Order #{order.Id}").FontSize(11).FontColor(Colors.White);
                    });

                    col.Item().Padding(24).Column(body =>
                    {
                        // ---- Order meta ----
                        body.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Billed to").FontSize(8).FontColor(mutedText);
                                c.Item().Text(customerName).Bold();
                            });

                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().AlignRight().Text("Date").FontSize(8).FontColor(mutedText);
                                c.Item().AlignRight().Text($"{order.CreatedAt:dd MMM yyyy, hh:mm tt}");
                            });
                        });

                        body.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Payment Method").FontSize(8).FontColor(mutedText);
                                c.Item().Text(order.PaymentMethod.ToString()).Bold();
                            });

                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().AlignRight().Text("Status").FontSize(8).FontColor(mutedText);
                                c.Item().AlignRight().Text(order.PaymentStatus.ToString()).Bold().FontColor(primaryColor);
                            });
                        });

                        body.Item().PaddingVertical(16).LineHorizontal(1).LineColor(borderColor);

                        // ---- Items table ----
                        body.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(4);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1.5f);
                                c.RelativeColumn(1.5f);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Text("ITEM").FontSize(8).FontColor(mutedText).Bold();
                                h.Cell().AlignCenter().Text("QTY").FontSize(8).FontColor(mutedText).Bold();
                                h.Cell().AlignRight().Text("PRICE").FontSize(8).FontColor(mutedText).Bold();
                                h.Cell().AlignRight().Text("TOTAL").FontSize(8).FontColor(mutedText).Bold();

                                h.Cell().ColumnSpan(4).PaddingTop(6).PaddingBottom(6)
                                    .BorderBottom(1).BorderColor(borderColor);
                            });

                            var isAlternate = false;
                            foreach (var item in order.OrderItems)
                            {
                                string bg = isAlternate ? "#FAFBFD" : Colors.White;
                                isAlternate = !isAlternate;

                                table.Cell().Background(bg).PaddingVertical(8).Text(item.ProductNameSnapshot).Bold();
                                table.Cell().Background(bg).PaddingVertical(8).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Background(bg).PaddingVertical(8).AlignRight().Text($"RM {item.UnitPriceSnapshot:0.00}");
                                table.Cell().Background(bg).PaddingVertical(8).AlignRight().Text($"RM {item.LineTotal:0.00}").Bold();

                                foreach (var mod in item.SelectedModifiers)
                                {
                                    table.Cell().Background(bg).PaddingBottom(6).Text($"   + {mod.ModifierOptionNameSnapshot}").FontSize(8).FontColor(mutedText);
                                    table.Cell().Background(bg);
                                    table.Cell().Background(bg);
                                    table.Cell().Background(bg).AlignRight().Text($"RM {mod.ExtraPriceSnapshot:0.00}").FontSize(8).FontColor(mutedText);
                                }
                            }
                        });

                        body.Item().PaddingVertical(16).LineHorizontal(1).LineColor(borderColor);

                        // ---- Totals ----
                        body.Item().AlignRight().Width(220).Column(totals =>
                        {
                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Subtotal").FontColor(mutedText);
                                r.ConstantItem(90).AlignRight().Text($"RM {order.Subtotal:0.00}");
                            });
                            totals.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem().Text("SST (6%)").FontColor(mutedText);
                                r.ConstantItem(90).AlignRight().Text($"RM {sst:0.00}");
                            });

                            if (order.DiscountAmount > 0)
                            {
                                totals.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().Text($"Voucher ({order.Voucher?.Code})").FontColor("#1E8E3E");
                                    r.ConstantItem(90).AlignRight().Text($"-RM {order.DiscountAmount:0.00}").FontColor("#1E8E3E");
                                });
                            }

                            totals.Item().PaddingTop(10).PaddingBottom(10).LineHorizontal(1).LineColor(borderColor);

                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Total").Bold().FontSize(13);
                                r.ConstantItem(90).AlignRight().Text($"RM {order.Total:0.00}").Bold().FontSize(13).FontColor(primaryColor);
                            });
                        });
                    });

                    // ---- Footer ----
                    col.Item().PaddingTop(20).Padding(24).AlignCenter().Column(footer =>
                    {
                        footer.Item().AlignCenter().Text("Thank you for your order!").FontSize(11).Bold();
                        footer.Item().AlignCenter().PaddingTop(2).Text("This is a computer-generated receipt.").FontSize(8).FontColor(mutedText);
                    });
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