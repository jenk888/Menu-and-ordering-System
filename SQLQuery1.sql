DELETE FROM dbo.Categories;
DBCC CHECKIDENT ('dbo.Categories', RESEED, 0);
