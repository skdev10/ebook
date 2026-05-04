using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services
{
    public class ChapterDataExtractor
    {
        public static Dictionary<string, object> ExtractChapterData(object response)
        {
            var result = new Dictionary<string, object>();

            try
            {
                // Handle different input types
                JObject jsonObj = null;
                
                if (response is string jsonString)
                {
                    jsonObj = JObject.Parse(jsonString);
                }
                else if (response is JObject jObj)
                {
                    jsonObj = jObj;
                }
                else if (response is Dictionary<string, object> dict)
                {
                    // Convert Dictionary to JObject
                    jsonObj = JObject.FromObject(dict);
                }
                else
                {
                    // Try to serialize and parse
                    jsonObj = JObject.FromObject(response);
                }

                if (jsonObj == null)
                {
                    Console.WriteLine("⚠️ ChapterDataExtractor: Failed to parse JSON object");
                    return result;
                }

                // Debug: Log the structure of the JSON
                Console.WriteLine($"🔍 ChapterDataExtractor: JSON keys: {string.Join(", ", jsonObj.Properties().Select(p => p.Name))}");
                
                // Check if "data" field exists
                var dataToken = jsonObj["data"];
                if (dataToken == null)
                {
                    Console.WriteLine("⚠️ ChapterDataExtractor: No 'data' field found in response");
                    Console.WriteLine($"🔍 Available fields: {string.Join(", ", jsonObj.Properties().Select(p => p.Name))}");
                    
                    // Try to use the root object itself if it has the expected fields
                    if (jsonObj["style"] != null || jsonObj["content"] != null || jsonObj["suggest_chapter_name"] != null)
                    {
                        Console.WriteLine("ℹ️ ChapterDataExtractor: Using root object as data (no 'data' wrapper)");
                        dataToken = jsonObj;
                    }
                    else
                    {
                        return result;
                    }
                }

                JObject data = null;
                
                // Handle different types of "data" field
                if (dataToken.Type == JTokenType.String)
                {
                    // If data is a string, check if it's JSON or HTML
                    var dataString = dataToken.ToString().Trim();
                    
                    // Check if it's HTML (starts with <) or JSON (starts with { or [)
                    if (dataString.StartsWith("<"))
                    {
                        // It's HTML content, create a wrapper object with the content
                        Console.WriteLine("ℹ️ ChapterDataExtractor: 'data' field is HTML content, wrapping it...");
                        data = new JObject
                        {
                            ["content"] = dataString
                        };
                        Console.WriteLine($"✅ ChapterDataExtractor: Wrapped HTML content (length: {dataString.Length})");
                    }
                    else if (dataString.StartsWith("{") || dataString.StartsWith("["))
                    {
                        // It's JSON, try to parse it
                        try
                        {
                            Console.WriteLine("ℹ️ ChapterDataExtractor: 'data' field is a JSON string, parsing...");
                            data = JObject.Parse(dataString);
                            Console.WriteLine($"✅ ChapterDataExtractor: Successfully parsed JSON string");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ ChapterDataExtractor: Error parsing JSON string: {ex.Message}");
                            // If parsing fails, treat it as content
                            Console.WriteLine("ℹ️ ChapterDataExtractor: Treating as content instead");
                            data = new JObject
                            {
                                ["content"] = dataString
                            };
                        }
                    }
                    else
                    {
                        // Plain text or other format, treat as content
                        Console.WriteLine("ℹ️ ChapterDataExtractor: 'data' field is plain text, treating as content...");
                        data = new JObject
                        {
                            ["content"] = dataString
                        };
                    }
                }
                else if (dataToken.Type == JTokenType.Object)
                {
                    // If data is already an object, use it directly
                    data = dataToken as JObject;
                    Console.WriteLine("ℹ️ ChapterDataExtractor: 'data' field is already an object");
                }
                else
                {
                    Console.WriteLine($"⚠️ ChapterDataExtractor: 'data' field is of unexpected type: {dataToken.Type}");
                    // Try to convert to string and use as content
                    try
                    {
                        var dataString = dataToken.ToString();
                        data = new JObject
                        {
                            ["content"] = dataString
                        };
                        Console.WriteLine("ℹ️ ChapterDataExtractor: Converted to content");
                    }
                    catch
                    {
                        return result;
                    }
                }

                if (data == null)
                {
                    Console.WriteLine("⚠️ ChapterDataExtractor: Failed to get data as JObject");
                    return result;
                }
                
                Console.WriteLine($"✅ ChapterDataExtractor: Successfully extracted data object with keys: {string.Join(", ", data.Properties().Select(p => p.Name))}");

                // Extract style
                var style = data["style"]?.ToString();
                result["style"] = style ?? string.Empty;

                // Extract suggest_chapter_name
                var suggestChapterName = data["suggest_chapter_name"]?.ToString();
                result["suggest_chapter_name"] = suggestChapterName ?? string.Empty;

                // Extract content
                var content = data["content"]?.ToString();
                result["content"] = content ?? string.Empty;

                // Extract highlights
                var highlightsToken = data["highlights"];
                object highlights = null;

                if (highlightsToken != null)
                {
                    if (highlightsToken.Type == JTokenType.String)
                    {
                        // If highlights is a string, try to parse it as JSON
                        try
                        {
                            var highlightsString = highlightsToken.ToString();
                            var highlightsObj = JObject.Parse(highlightsString);
                            highlights = highlightsObj.ToObject<Dictionary<string, object>>();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ ChapterDataExtractor: Error parsing highlights string: {ex.Message}");
                            // If parsing fails, use the string as-is
                            highlights = highlightsToken.ToString();
                        }
                    }
                    else if (highlightsToken.Type == JTokenType.Object)
                    {
                        // If highlights is already an object, convert it
                        highlights = highlightsToken.ToObject<Dictionary<string, object>>();
                    }
                    else
                    {
                        // For other types, convert to string
                        highlights = highlightsToken.ToString();
                    }
                }

                result["highlights"] = highlights;

                // Extract highlight_of_previous_chapter from highlights
                // Save the entire highlights content to highlight_of_previous_chapter
                string highlightOfPreviousChapter = string.Empty;
                if (highlightsToken != null)
                {
                    // Get the raw highlights content directly from the token
                    if (highlightsToken.Type == JTokenType.String)
                    {
                        // If highlights is a string, use it directly (could be HTML or JSON string)
                        highlightOfPreviousChapter = highlightsToken.ToString();
                        Console.WriteLine($"ℹ️ ChapterDataExtractor: Highlights is a string, length: {highlightOfPreviousChapter.Length}");
                    }
                    else if (highlightsToken.Type == JTokenType.Object)
                    {
                        // If highlights is an object, try to get detailed_bullet_summary first, otherwise serialize the whole object
                        var highlightsObj = highlightsToken as JObject;
                        if (highlightsObj != null)
                        {
                            // Try to get detailed_bullet_summary (the main content)
                            var detailedBulletSummary = highlightsObj["detailed_bullet_summary"];
                            if (detailedBulletSummary != null)
                            {
                                highlightOfPreviousChapter = detailedBulletSummary.ToString();
                                Console.WriteLine($"ℹ️ ChapterDataExtractor: Found detailed_bullet_summary, length: {highlightOfPreviousChapter.Length}");
                            }
                            else
                            {
                                // If no detailed_bullet_summary, serialize the entire highlights object
                                highlightOfPreviousChapter = highlightsObj.ToString(Formatting.None);
                                Console.WriteLine($"ℹ️ ChapterDataExtractor: Serialized entire highlights object, length: {highlightOfPreviousChapter.Length}");
                            }
                        }
                    }
                    else
                    {
                        // For other types, convert to string
                        highlightOfPreviousChapter = highlightsToken.ToString();
                        Console.WriteLine($"ℹ️ ChapterDataExtractor: Converted highlights to string, length: {highlightOfPreviousChapter.Length}");
                    }
                }
                else if (highlights != null)
                {
                    // Fallback: use the processed highlights object
                    if (highlights is Dictionary<string, object> highlightsDict)
                    {
                        // Try to get detailed_bullet_summary first
                        if (highlightsDict.ContainsKey("detailed_bullet_summary"))
                        {
                            highlightOfPreviousChapter = highlightsDict["detailed_bullet_summary"]?.ToString() ?? string.Empty;
                        }
                        else
                        {
                            // Convert entire highlights object to JSON string
                            highlightOfPreviousChapter = JsonConvert.SerializeObject(highlightsDict);
                        }
                    }
                    else if (highlights is string highlightsString)
                    {
                        highlightOfPreviousChapter = highlightsString;
                    }
                    else
                    {
                        highlightOfPreviousChapter = highlights?.ToString() ?? string.Empty;
                    }
                }
                
                result["highlight_of_previous_chapter"] = highlightOfPreviousChapter;
                Console.WriteLine($"✅ ChapterDataExtractor: Extracted highlight_of_previous_chapter, length: {highlightOfPreviousChapter?.Length ?? 0}");

                // Set chapter_name from suggest_chapter_name
                result["chapter_name"] = suggestChapterName ?? string.Empty;

                // Content is already extracted above
                // result["content"] is already set

                Console.WriteLine($"✅ ChapterDataExtractor: Extracted - Chapter Name: {suggestChapterName}, Content Length: {content?.Length ?? 0}, Highlight Length: {highlightOfPreviousChapter?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ChapterDataExtractor Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }

            return result;
        }
    }
}
