-- Seed BookCoverPagePath for subfolders under Images/book_covers/
-- Project layout: eBookProjectGit25/Images/book_covers/
--   - business & Entrepreneurship
--   - Fantasy
--   - Health & Fitness
--
-- Ensure bookcoverpages has columns: Id, Title, Description, BookCoverPagePath, Status
-- Paths are relative to project root so the app can resolve: ContentRootPath + path

-- If your table already has rows, update by Title (adjust Id if needed):
UPDATE bookcoverpages SET BookCoverPagePath = 'Images/book_covers/Fantasy' WHERE Title = 'Fantasy';
UPDATE bookcoverpages SET BookCoverPagePath = 'Images/book_covers/business & Entrepreneurship' WHERE Title LIKE '%business%' OR Title LIKE '%Entrepreneurship%';
UPDATE bookcoverpages SET BookCoverPagePath = 'Images/book_covers/Health & Fitness' WHERE Title LIKE '%Health%'OR Title = 'Health & Fitness';

-- Or insert new rows (if table is empty or you want to add these):
-- INSERT INTO bookcoverpages (Title, Description, BookCoverPagePath, Status) VALUES
-- ('Fantasy', 'Fantasy book covers', 'Images/book_covers/Fantasy', 'Active'),
-- ('Business & Entrepreneurship', 'Business and entrepreneurship covers', 'Images/book_covers/business & Entrepreneurship', 'Active'),
-- ('Health & Fitness', 'Health and fitness covers', 'Images/book_covers/Health & Fitness', 'Active');
