//using edu.stanford.nlp.pipeline;
//using edu.stanford.nlp.ling;

//namespace DictionaryImporter.Core.Text.Experimental;

//public class StanfordDefinitionNormalizer
//{
//    public string NormalizeWithNLP(string text)
//    {
//        var props = new java.util.Properties();
//        props.setProperty("annotators", "tokenize,ssplit,pos,lemma,parse");
//        var pipeline = new StanfordCoreNLP(props);

//        var annotation = new Annotation(text);
//        pipeline.annotate(annotation);

//        // Lemmatization (get base forms)
//        var lemmas = new java.util.ArrayList();
//        var sentences = annotation.get(typeof(CoreAnnotations.SentencesAnnotation));
//        // Extract lemmatized text

//        return processedText;
//    }
//}