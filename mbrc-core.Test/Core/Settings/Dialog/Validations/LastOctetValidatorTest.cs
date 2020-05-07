using MusicBeeRemote.Core.Settings.Dialog.Validations;
using NUnit.Framework;

namespace MusicBeeRemote.Test.Core.Settings.Dialog.Validations
{
    [TestFixture]
    public class LastOctetValidatorTest
    {
        [Test]
        public void InvalidLastOctetZero()
        {
            Assert.IsFalse(LastOctetValidator.Validate("192.168.1.10", "0"));
        }

        [Test]
        public void InvalidLastOctetOverMax()
        {
            Assert.IsFalse(LastOctetValidator.Validate("192.168.1.10", "255"));
        }

        [Test]
        public void InvalidLastLessThanLastOfBaseIp()
        {
            Assert.IsFalse(LastOctetValidator.Validate("192.168.1.10", "8"));
        }

        [Test]
        public void ValidLastOctetValid()
        {
            Assert.IsTrue(LastOctetValidator.Validate("192.168.1.10", "20"));
        }

        [Test]
        public void InvalidBothInputsNull()
        {
            Assert.IsFalse(LastOctetValidator.Validate(null, null));
        }

        [Test]
        public void InvalidBothInputsEmpty()
        {
            Assert.IsFalse(LastOctetValidator.Validate(string.Empty, string.Empty));
        }
    }
}
