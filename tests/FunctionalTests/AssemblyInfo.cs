using Xunit;

// Fonksiyonel testler yapılandırmayı ortam değişkenleriyle veriyor; ortam
// değişkenleri süreç genelinde ortak olduğu için paralel çalışma yarışa yol açar.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
